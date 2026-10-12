<#
.SYNOPSIS
  microsoft/winget-pkgs 제출 PR 에 Needs-CLA 라벨이 붙으면, PR 을 연 계정으로 CLA 동의 댓글을 단다.

.DESCRIPTION
  winget-pkgs 는 그 저장소에 머지된 기여가 없는 사람(author_association 이 NONE·FIRST_TIMER·FIRST_TIME_CONTRIBUTOR)이 PR 을 열면
  실제 CLA 서명 여부와 상관없이 Needs-CLA 라벨을 붙이고(.github/policies/labelManagement.issueOpened.yml), PR 작성자가
  "@microsoft-github-policy-service agree" 라고 댓글을 달아야 뗀다(labelManagement.issueUpdated.yml). 라벨이 있으면 머지되지 않는다.
  첫 제출이 머지되면 기여자(CONTRIBUTOR)가 되어 라벨이 더는 붙지 않으므로, 그 뒤로는 이 스크립트가 할 일이 없다.

  저장소 소유자(-AllowedAuthor, kimch0627)는 Microsoft CLA 에 동의했고(microsoft/winget-pkgs#448648), 제출 PR 마다 이 댓글을
  자동으로 다는 것을 허락했다(2026-10-13). 그래서 토큰 계정이 그 사람이고 그 사람이 연 PR 일 때만 댓글을 단다.

  - 라벨은 PR 을 열고 30초쯤 뒤에 붙으므로, PR 을 연 때부터 -WaitSeconds 동안 기다린다. 그동안 붙지 않으면 아무것도 하지 않는다.
  - 이미 동의 댓글이 있으면 다시 달지 않고 라벨이 떨어지기만 기다린다.
  - gh CLI 를 쓴다. 토큰(GH_TOKEN 또는 gh auth login)은 PR 을 연 계정의 것이어야 한다(public_repo 권한이면 된다).
  - 할 일이 없거나 댓글을 달 수 없는 경우(다른 계정, 닫힌 PR, PR 을 못 찾음)는 경고만 남기고 성공으로 끝난다. gh 호출 자체가 실패하면 실패한다.

.EXAMPLE
  ./tools/winget/Send-WingetClaAgreement.ps1 -Pr 450924
  ./tools/winget/Send-WingetClaAgreement.ps1 -Version 1.16.1       # 그 버전의 열린 제출 PR 을 찾아서
  ./tools/winget/Send-WingetClaAgreement.ps1 -Pr 450924 -DryRun    # 댓글은 달지 않고 판단만 보여 준다
#>
[CmdletBinding(DefaultParameterSetName = 'Pr')]
param(
    [Parameter(Mandatory, ParameterSetName = 'Pr')] [int] $Pr,
    [Parameter(Mandatory, ParameterSetName = 'Version')] [string] $Version,
    [string] $Repo = 'microsoft/winget-pkgs',
    [string] $PackageId = 'kimch0627.ImeBadge',
    [string] $AllowedAuthor = 'kimch0627',
    [int] $WaitSeconds = 180,
    [int] $PollSeconds = 10,
    [switch] $DryRun
)

$ErrorActionPreference = 'Stop'
$Agree = '@microsoft-github-policy-service agree'
$Label = 'Needs-CLA'

function Invoke-Gh([string[]] $GhArgs) {
    $out = & gh @GhArgs
    if ($LASTEXITCODE -ne 0) { throw "gh $($GhArgs[0..1] -join ' ') 실패 (exit $LASTEXITCODE)" }
    $out -join "`n"
}

function Write-Note([string] $Message, [switch] $Warn) {
    if (-not $Warn) { Write-Host $Message }
    elseif ($env:GITHUB_ACTIONS) { Write-Host "::warning::$Message" }
    else { Write-Warning $Message }
}

# 토큰 계정이 연 열린 PR 가운데 제목이 "… <패키지> version <버전>" 인 것(검색 색인을 거치지 않아 막 연 PR 도 바로 보인다).
function Find-SubmissionPr {
    $q = 'query($n:Int!){ viewer { pullRequests(first:$n, states:OPEN, orderBy:{field:CREATED_AT, direction:DESC}) { nodes { number title repository { nameWithOwner } } } } }'
    $nodes = (Invoke-Gh @('api', 'graphql', '-f', "query=$q", '-F', 'n=30') | ConvertFrom-Json).data.viewer.pullRequests.nodes
    $want = "$PackageId version $Version"
    $hit = $nodes | Where-Object { $_.repository.nameWithOwner -eq $Repo -and $_.title.EndsWith($want) } | Select-Object -First 1
    if ($hit) { [int] $hit.number } else { 0 }
}

function Get-Pr { Invoke-Gh @('pr', 'view', "$Pr", '-R', $Repo, '--json', 'url,state,author,labels,comments') | ConvertFrom-Json }
function Test-Label($p) { [bool] ($p.labels | Where-Object { $_.name -eq $Label }) }
function Test-Agreed($p) { [bool] ($p.comments | Where-Object { $_.author.login -eq $me -and $_.body -match [regex]::Escape($Agree) }) }

$me = (Invoke-Gh @('api', 'user', '--jq', '.login')).Trim()
if ($me -ne $AllowedAuthor) {
    Write-Note "토큰 계정($me)이 CLA 동의를 맡긴 계정($AllowedAuthor)이 아니라 댓글을 달지 않는다." -Warn
    exit 0
}

if ($PSCmdlet.ParameterSetName -eq 'Version') {
    $Version = $Version.TrimStart('v')
    $findBy = [DateTimeOffset]::UtcNow.AddSeconds($WaitSeconds)
    while (($Pr = Find-SubmissionPr) -eq 0) {
        if ([DateTimeOffset]::UtcNow -gt $findBy) {
            Write-Note "$Repo 에서 $me 가 연 '$PackageId version $Version' 제출 PR 을 찾지 못했다." -Warn
            exit 0
        }
        Start-Sleep -Seconds $PollSeconds
    }
}

$p = Get-Pr
Write-Host "PR: $($p.url) (작성자 $($p.author.login), $($p.state))"
if ($p.author.login -ne $me) {
    Write-Note "PR 작성자($($p.author.login))가 토큰 계정($me)과 달라 댓글을 달지 않는다(라벨은 PR 작성자의 댓글로만 떨어진다)." -Warn
    exit 0
}
if ($p.state -ne 'OPEN') {
    Write-Note "열린 PR 이 아니라($($p.state)) 할 일이 없다."
    exit 0
}

# 라벨은 PR 을 열 때 한 번 붙는다. 연 지 -WaitSeconds 가 지나도록 없으면 붙지 않은 것(기여자로 인정됨)이거나 이미 떨어진 것.
$created = [DateTimeOffset]::Parse((Invoke-Gh @('pr', 'view', "$Pr", '-R', $Repo, '--json', 'createdAt', '--jq', '.createdAt')).Trim(),
    [Globalization.CultureInfo]::InvariantCulture)
$labelBy = $created.AddSeconds($WaitSeconds)
while (-not (Test-Label $p)) {
    if ([DateTimeOffset]::UtcNow -gt $labelBy) {
        $why = if (Test-Agreed $p) { '이미 동의 댓글로 떨어졌다' } else { '붙지 않았다(기여자로 인정됨)' }
        Write-Note "$Label 라벨이 없다: $why. 할 일이 없다."
        exit 0
    }
    Start-Sleep -Seconds $PollSeconds
    $p = Get-Pr
}

if (Test-Agreed $p) {
    Write-Note "$Label 라벨이 있지만 동의 댓글은 이미 있다. 라벨이 떨어지기를 기다린다."
}
elseif ($DryRun) {
    Write-Note "[DryRun] $Label 라벨이 있다. 댓글을 달 차례: $Agree"
    exit 0
}
else {
    Invoke-Gh @('pr', 'comment', "$Pr", '-R', $Repo, '--body', $Agree) | Out-Null
    Write-Note "$Label 라벨이 있어 동의 댓글을 달았다: $Agree"
}

# 정책 서비스가 라벨을 떼기까지 보통 20초 안팎.
$removeBy = [DateTimeOffset]::UtcNow.AddSeconds(120)
while (Test-Label $p) {
    if ([DateTimeOffset]::UtcNow -gt $removeBy) {
        Write-Note "동의 댓글 뒤에도 $Label 라벨이 남아 있다. PR 을 확인하세요: $($p.url)" -Warn
        exit 0
    }
    Start-Sleep -Seconds $PollSeconds
    $p = Get-Pr
}
Write-Note "$Label 라벨이 떨어졌다."
