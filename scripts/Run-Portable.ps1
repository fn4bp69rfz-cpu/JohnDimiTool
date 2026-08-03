param(
  [string]$Repo = 'fn4bp69rfz-cpu/JohnDimiTool',
  [string]$AssetName = 'PcSetupMaintainer.exe',
  [string]$RunRoot = "$env:TEMP\PcSetupMaintainer-Portable",
  [switch]$NoAdmin
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

trap {
  Write-Host ''
  Write-Host 'PC Setup Maintainer portable launch failed.' -ForegroundColor Red
  Write-Host $_.Exception.Message -ForegroundColor Red
  if ($_.ScriptStackTrace) {
    Write-Host $_.ScriptStackTrace -ForegroundColor DarkGray
  }
  exit 1
}

function Write-Step {
  param([Parameter(Mandatory)][string]$Message)
  Write-Host "[JohnDimiTool] $Message"
}

function Test-IsAdministrator {
  $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
  $principal = [Security.Principal.WindowsPrincipal]::new($identity)
  return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Get-GitHubJson {
  param([Parameter(Mandatory)][string]$Uri)
  Invoke-RestMethod -Uri $Uri -Headers @{
    'Accept' = 'application/vnd.github+json'
    'User-Agent' = 'JohnDimiToolPortable'
    'X-GitHub-Api-Version' = '2022-11-28'
  }
}

function Get-ExpectedSha256 {
  param(
    [Parameter(Mandatory)]$Release,
    [Parameter(Mandatory)]$Asset,
    [Parameter(Mandatory)][string]$TempDir
  )

  if ($Asset.digest -and ($Asset.digest -match '^sha256:([a-fA-F0-9]{64})$')) {
    return $Matches[1].ToLowerInvariant()
  }

  $checksumAsset = $Release.assets |
    Where-Object { $_.name -in @("$($Asset.name).sha256", "$($Asset.name).sha256sum", 'SHA256SUMS', 'checksums.txt') } |
    Select-Object -First 1

  if (-not $checksumAsset) {
    throw "The latest release does not provide a SHA-256 checksum for $($Asset.name)."
  }

  $checksumPath = Join-Path $TempDir $checksumAsset.name
  Invoke-WebRequest -Uri $checksumAsset.browser_download_url -OutFile $checksumPath
  $checksumText = Get-Content -LiteralPath $checksumPath -Raw
  if ($checksumText -notmatch '([a-fA-F0-9]{64})') {
    throw "Checksum asset $($checksumAsset.name) did not contain a valid SHA-256 hash."
  }

  return $Matches[1].ToLowerInvariant()
}

[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

if (-not $NoAdmin -and -not (Test-IsAdministrator)) {
  Write-Step 'Restarting as Administrator'
  $launcher = Join-Path ([System.IO.Path]::GetTempPath()) ('JohnDimiTool-Portable-' + [guid]::NewGuid().ToString('N') + '.ps1')
  (Invoke-WebRequest -Uri "https://raw.githubusercontent.com/$Repo/main/scripts/Run-Portable.ps1" -UseBasicParsing).Content |
    Set-Content -LiteralPath $launcher -Encoding UTF8
  Start-Process powershell.exe -Verb RunAs -ArgumentList "-NoProfile -ExecutionPolicy Bypass -File `"$launcher`""
  return
}

Write-Step "Portable mode. No install, no shortcuts, no Start Menu entries."
Write-Step "Resolving latest release from https://github.com/$Repo"
$release = Get-GitHubJson -Uri "https://api.github.com/repos/$Repo/releases/latest"
$asset = $release.assets | Where-Object { $_.name -eq $AssetName } | Select-Object -First 1
if (-not $asset) {
  $available = ($release.assets | ForEach-Object name) -join ', '
  if (-not $available) { $available = 'none' }
  throw "Latest release '$($release.tag_name)' does not contain $AssetName. Available assets: $available"
}

$sessionRoot = Join-Path $RunRoot $release.tag_name
New-Item -ItemType Directory -Force -Path $sessionRoot | Out-Null
$exePath = Join-Path $sessionRoot $AssetName

$shouldDownload = $true
if (Test-Path -LiteralPath $exePath) {
  Write-Step 'Existing portable EXE found. Verifying before reuse.'
  $expectedHash = Get-ExpectedSha256 -Release $release -Asset $asset -TempDir $sessionRoot
  $existingHash = (Get-FileHash -LiteralPath $exePath -Algorithm SHA256).Hash.ToLowerInvariant()
  if ($existingHash -eq $expectedHash) {
    $shouldDownload = $false
    Write-Step "Using cached verified portable EXE: $exePath"
  }
}

if ($shouldDownload) {
  Write-Step "Downloading $AssetName from release $($release.tag_name)"
  Invoke-WebRequest -Uri $asset.browser_download_url -OutFile $exePath
  Write-Step 'Verifying SHA-256 checksum'
  $expectedHash = Get-ExpectedSha256 -Release $release -Asset $asset -TempDir $sessionRoot
  $actualHash = (Get-FileHash -LiteralPath $exePath -Algorithm SHA256).Hash.ToLowerInvariant()
  if ($actualHash -ne $expectedHash) {
    Remove-Item -LiteralPath $exePath -Force -ErrorAction SilentlyContinue
    throw "Checksum mismatch. Expected $expectedHash but downloaded $actualHash."
  }
  Write-Step "Verified SHA-256: $actualHash"
}

Write-Step "Launching portable app from $exePath"
Start-Process -FilePath $exePath -WorkingDirectory $sessionRoot
