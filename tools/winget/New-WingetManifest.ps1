<#
.SYNOPSIS
  winget 매니페스트를 템플릿(winget/templates)에서 만든다.

.DESCRIPTION
  GitHub Release 의 SHA256SUMS.txt 에서 설치 프로그램 해시를 읽어 {{VERSION}} {{SHA256}} {{DATE}} 를 채운다.
  결과는 winget-pkgs 저장소 구조(manifests/k/kimch0627/ImeBadge/<버전>/)로 놓인다.

.EXAMPLE
  ./tools/winget/New-WingetManifest.ps1 -Version 1.0.0
  ./tools/winget/New-WingetManifest.ps1 -Version 1.0.0 -Sha256 <해시> -OutDir out/winget
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $Version,
    [string] $Sha256,
    [string] $ReleaseDate = (Get-Date -Format 'yyyy-MM-dd'),
    [string] $OutDir = 'winget/manifests',
    [string] $Repo = 'kimch0627/windows-ime-badge',
    [int] $RetrySeconds = 300
)

$ErrorActionPreference = 'Stop'
$Version = $Version.TrimStart('v')
$root = Resolve-Path (Join-Path $PSScriptRoot '..' '..')
$templates = Join-Path $root 'winget' 'templates'
$installerName = "ImeBadge-Setup-$Version.exe"

if (-not $Sha256) {
    # 릴리스 첨부가 올라오기 직전일 수 있어 잠시 재시도한다.
    $url = "https://github.com/$Repo/releases/download/v$Version/SHA256SUMS.txt"
    $deadline = (Get-Date).AddSeconds($RetrySeconds)
    while ($true) {
        try {
            $sums = (Invoke-WebRequest -Uri $url -UseBasicParsing).Content
            break
        }
        catch {
            if ((Get-Date) -gt $deadline) { throw "SHA256SUMS.txt 를 받지 못했습니다: $url ($_)" }
            Write-Host "SHA256SUMS.txt 대기 중... ($url)"
            Start-Sleep -Seconds 15
        }
    }
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
"manifest_dir=$dest" | Out-File -FilePath $env:GITHUB_OUTPUT -Append -ErrorAction SilentlyContinue
