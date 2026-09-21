<#
.SYNOPSIS
  winget 매니페스트를 템플릿(winget/templates)에서 만든다.

.DESCRIPTION
  GitHub Release 의 SHA256SUMS.txt 에서 설치 프로그램 해시를 읽어 {{VERSION}} {{SHA256}} {{DATE}} 를 채운다.
  결과는 winget-pkgs 저장소 구조(manifests/k/kimch0627/ImeBadge/<버전>/)로 놓인다.

  SHA256SUMS.txt 는 GITHUB_TOKEN 환경 변수가 있으면 GitHub API(비공개 저장소에서도 동작)로, 없으면 공개 다운로드 주소로 받는다.
  -Version 을 비우면 최신 정식 릴리스(API 의 releases/latest)를 쓴다.

.EXAMPLE
  ./tools/winget/New-WingetManifest.ps1 -Version 1.0.0
  ./tools/winget/New-WingetManifest.ps1 -Version 1.0.0 -Sha256 <해시> -OutDir out/winget
#>
[CmdletBinding()]
param(
    [string] $Version = '',
    [string] $Sha256,
    [string] $ReleaseDate = (Get-Date -Format 'yyyy-MM-dd'),
    [string] $OutDir = 'winget/manifests',
    [string] $Repo = 'kimch0627/windows-ime-badge',
    [int] $RetrySeconds = 300
)

$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..' '..')
$templates = Join-Path $root 'winget' 'templates'
$api = "https://api.github.com/repos/$Repo"
$headers = @{ 'Accept' = 'application/vnd.github+json'; 'User-Agent' = 'ImeBadge-winget-manifest' }
if ($env:GITHUB_TOKEN) { $headers['Authorization'] = "Bearer $env:GITHUB_TOKEN" }

function Invoke-WithRetry([scriptblock] $Action, [string] $What) {
    $deadline = (Get-Date).AddSeconds($RetrySeconds)
    while ($true) {
        try { return & $Action }
        catch {
            if ((Get-Date) -gt $deadline) { throw "$What 실패: $_" }
            Write-Host "$What 대기 중... ($_)"
            Start-Sleep -Seconds 15
        }
    }
}

if (-not $Version) {
    $latest = Invoke-WithRetry { Invoke-RestMethod -Uri "$api/releases/latest" -Headers $headers } '최신 릴리스 조회'
    $Version = $latest.tag_name
    Write-Host "version not given; using latest release $Version"
}
$Version = $Version.TrimStart('v')
$installerName = "ImeBadge-Setup-$Version.exe"

if (-not $Sha256) {
    # 릴리스 첨부가 올라오기 직전일 수 있어 잠시 재시도한다.
    $sums = Invoke-WithRetry {
        if ($env:GITHUB_TOKEN) {
            # API 로 첨부 파일을 찾아 받는다 (비공개 저장소에서도 동작).
            $rel = Invoke-RestMethod -Uri "$api/releases/tags/v$Version" -Headers $headers
            $asset = $rel.assets | Where-Object { $_.name -eq 'SHA256SUMS.txt' } | Select-Object -First 1
            if (-not $asset) { throw "릴리스 v$Version 에 SHA256SUMS.txt 가 아직 없습니다." }
            $h = $headers.Clone(); $h['Accept'] = 'application/octet-stream'
            (Invoke-WebRequest -Uri $asset.url -Headers $h -UseBasicParsing).Content
        }
        else {
            (Invoke-WebRequest -Uri "https://github.com/$Repo/releases/download/v$Version/SHA256SUMS.txt" -UseBasicParsing).Content
        }
    } 'SHA256SUMS.txt 다운로드'
    if ($sums -is [byte[]]) { $sums = [System.Text.Encoding]::UTF8.GetString($sums) }
    $line = ($sums -split "`n") | Where-Object { $_ -match [regex]::Escape($installerName) } | Select-Object -First 1
    if (-not $line) { throw "SHA256SUMS.txt 에 $installerName 이 없습니다." }
    $Sha256 = ($line -split '\s+')[0]
}
$Sha256 = $Sha256.ToUpperInvariant()
if ($Sha256 -notmatch '^[0-9A-F]{64}$') { throw "SHA256 형식이 아닙니다: $Sha256" }

$dest = Join-Path $root $OutDir 'k' 'kimch0627' 'ImeBadge' $Version
New-Item -ItemType Directory -Force -Path $dest | Out-Null

Get-ChildItem $templates -Filter '*.yaml' | ForEach-Object {
    $text = Get-Content $_.FullName -Raw
    $text = $text.Replace('{{VERSION}}', $Version).Replace('{{SHA256}}', $Sha256).Replace('{{DATE}}', $ReleaseDate)
    $target = Join-Path $dest $_.Name
    # winget-pkgs 는 UTF-8(BOM 없음) 을 기대한다.
    [System.IO.File]::WriteAllText($target, $text, (New-Object System.Text.UTF8Encoding($false)))
    Write-Host "wrote $target"
}
Write-Host "manifest dir: $dest"
if ($env:GITHUB_OUTPUT) {
    "manifest_dir=$dest" | Out-File -FilePath $env:GITHUB_OUTPUT -Append
    "version=$Version" | Out-File -FilePath $env:GITHUB_OUTPUT -Append
}
