<#
.SYNOPSIS
  winget 매니페스트를 템플릿(winget/templates)에서 만든다.

.DESCRIPTION
  GitHub Release 의 SHA256SUMS.txt 에서 아키텍처(x64, arm64)별 설치 프로그램 해시를 읽어
  {{VERSION}} {{SHA256_X64}} {{SHA256_ARM64}} {{DATE}} 를 채운다.
  결과는 winget-pkgs 저장소 구조(manifests/k/kimch0627/ImeBadge/<버전>/)로 놓인다.

  SHA256SUMS.txt 는 GITHUB_TOKEN 환경 변수가 있으면 GitHub API(비공개 저장소에서도 동작)로, 없으면 공개 다운로드 주소로 받는다.
  -Version 을 비우면 최신 정식 릴리스(API 의 releases/latest)를 쓴다.
  -Sha256X64 / -Sha256Arm64 를 둘 다 주면 SHA256SUMS.txt 를 받지 않는다.

.EXAMPLE
  ./tools/winget/New-WingetManifest.ps1 -Version 1.0.0
  ./tools/winget/New-WingetManifest.ps1 -Version 1.0.0 -Sha256X64 <해시> -Sha256Arm64 <해시> -OutDir out/winget
#>
[CmdletBinding()]
param(
    [string] $Version = '',
    [string] $Sha256X64,
    [string] $Sha256Arm64,
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

# SHA256SUMS.txt 본문에서 파일 이름이 줄 끝에 있는 줄의 해시를 돌려준다(없으면 $null).
# 파일 이름 앞에 공백을 요구해 "-x64.exe" 가 "-arm64.exe" 줄에 걸리지 않게 한다.
function Find-Sum([string] $Sums, [string] $File) {
    $line = ($Sums -split "`n") | Where-Object { $_ -match ('\s' + [regex]::Escape($File) + '\s*$') } | Select-Object -First 1
    if ($line) { ($line.Trim() -split '\s+')[0] } else { $null }
}

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
# 아키텍처 → (설치 프로그램 파일 이름, 템플릿 자리표시자, 명령줄로 받은 해시). 템플릿의 {{FILE_*}} {{SHA256_*}} 를 채운다.
$archs = [ordered]@{
    x64   = @{ File = "ImeBadge-Setup-$Version-x64.exe";   FilePlaceholder = '{{FILE_X64}}';   ShaPlaceholder = '{{SHA256_X64}}';   Sha256 = $Sha256X64 }
    arm64 = @{ File = "ImeBadge-Setup-$Version-arm64.exe"; FilePlaceholder = '{{FILE_ARM64}}'; ShaPlaceholder = '{{SHA256_ARM64}}'; Sha256 = $Sha256Arm64 }
}
# 1.2.0 이전 릴리스는 x64 설치 프로그램 하나뿐이고 이름에 "-x64" 접미사가 없다. 그 릴리스로 매니페스트를 다시 만들 때
# (수동 제출, winget 파일을 바꾼 브랜치 푸시의 검증)는 옛 이름을 쓰고 arm64 항목을 뺀다.
$legacyFile = "ImeBadge-Setup-$Version.exe"
$legacy = $false

if ($archs.Values | Where-Object { -not $_.Sha256 }) {
    # 릴리스 첨부가 올라오기 직전일 수 있어 잠시 재시도한다.
    $sums = Invoke-WithRetry {
        if ($env:GITHUB_TOKEN) {
            # API 로 첨부 파일을 찾아 받는다 (비공개 저장소에서도 동작).
            $rel = Invoke-RestMethod -Uri "$api/releases/tags/v$Version" -Headers $headers
            $asset = $rel.assets | Where-Object { $_.name -eq 'SHA256SUMS.txt' } | Select-Object -First 1
            if (-not $asset) { throw "릴리스 v$Version 에 SHA256SUMS.txt 가 아직 없습니다." }
            $h = $headers.Clone(); $h['Accept'] = 'application/octet-stream'
            # Content 는 byte[] 다. 함수 밖으로 나가면 PowerShell 이 배열을 요소별로 풀어 버리므로 여기서 바로 문자열로 만든다.
            [System.Text.Encoding]::UTF8.GetString((Invoke-WebRequest -Uri $asset.url -Headers $h -UseBasicParsing).Content)
        }
        else {
            [string](Invoke-WebRequest -Uri "https://github.com/$Repo/releases/download/v$Version/SHA256SUMS.txt" -UseBasicParsing).Content
        }
    } 'SHA256SUMS.txt 다운로드'
    $sums = [string]$sums
    if (-not (Find-Sum $sums $archs.x64.File) -and (Find-Sum $sums $legacyFile)) {
        Write-Host "legacy release (x64 only, no arch suffix): $legacyFile"
        $legacy = $true
        $archs.x64.File = $legacyFile
        $archs.Remove('arm64')
    }
    foreach ($a in $archs.Values) {
        if ($a.Sha256) { continue }
        $a.Sha256 = Find-Sum $sums $a.File
        if (-not $a.Sha256) { throw "SHA256SUMS.txt 에 $($a.File) 이 없습니다." }
    }
}
foreach ($a in $archs.Values) {
    $a.Sha256 = $a.Sha256.ToUpperInvariant()
    if ($a.Sha256 -notmatch '^[0-9A-F]{64}$') { throw "SHA256 형식이 아닙니다 ($($a.File)): $($a.Sha256)" }
}

$dest = Join-Path $root $OutDir 'k' 'kimch0627' 'ImeBadge' $Version
New-Item -ItemType Directory -Force -Path $dest | Out-Null

Get-ChildItem $templates -Filter '*.yaml' | ForEach-Object {
    $text = Get-Content $_.FullName -Raw
    if ($legacy) {
        # "  - Architecture: arm64" 로 시작하는 항목(그 아래 4칸 들여쓴 줄 포함)을 통째로 뺀다.
        $text = [regex]::Replace($text, '(?m)^  - Architecture: arm64\r?\n(?:    .*\r?\n)*', '')
    }
    $text = $text.Replace('{{VERSION}}', $Version).Replace('{{DATE}}', $ReleaseDate)
    foreach ($a in $archs.Values) { $text = $text.Replace($a.FilePlaceholder, $a.File).Replace($a.ShaPlaceholder, $a.Sha256) }
    if ($text -match '\{\{[A-Z0-9_]+\}\}') { throw "채우지 못한 자리표시자가 있습니다 ($($_.Name)): $($Matches[0])" }
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
