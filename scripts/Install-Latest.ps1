param(
  [string]$Repo = 'fn4bp69rfz-cpu/JohnDimiTool',
  [string]$AssetName = 'PcSetupMaintainer.exe',
  [string]$InstallDir = "$env:LOCALAPPDATA\Programs\PcSetupMaintainer",
  [switch]$NoLaunch
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

trap {
  Write-Host ''
  Write-Host 'PC Setup Maintainer installation failed.' -ForegroundColor Red
  Write-Host $_.Exception.Message -ForegroundColor Red
  if ($_.ScriptStackTrace) {
    Write-Host $_.ScriptStackTrace -ForegroundColor DarkGray
  }
  exit 1
}

function Write-Step {
  param([Parameter(Mandatory)][string]$Message)
  Write-Host "[PcSetupMaintainer] $Message"
}

function Get-GitHubJson {
  param([Parameter(Mandatory)][string]$Uri)
  Invoke-RestMethod -Uri $Uri -Headers @{
    'Accept' = 'application/vnd.github+json'
    'User-Agent' = 'PcSetupMaintainerInstaller'
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
    throw "The latest release does not provide a SHA-256 checksum for $($Asset.name). Upload $($Asset.name).sha256 next to the executable."
  }

  $checksumPath = Join-Path $TempDir $checksumAsset.name
  Invoke-WebRequest -Uri $checksumAsset.browser_download_url -OutFile $checksumPath
  $checksumText = Get-Content -LiteralPath $checksumPath -Raw
  if ($checksumText -notmatch '([a-fA-F0-9]{64})') {
    throw "Checksum asset $($checksumAsset.name) did not contain a valid SHA-256 hash."
  }

  return $Matches[1].ToLowerInvariant()
}

function New-Shortcut {
  param(
    [Parameter(Mandatory)][string]$Path,
    [Parameter(Mandatory)][string]$TargetPath,
    [string]$WorkingDirectory
  )

  $shell = New-Object -ComObject WScript.Shell
  $shortcut = $shell.CreateShortcut($Path)
  $shortcut.TargetPath = $TargetPath
  $shortcut.WorkingDirectory = $WorkingDirectory
  $shortcut.Description = 'PC Setup Maintainer'
  $shortcut.IconLocation = "$TargetPath,0"
  $shortcut.Save()
}

[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

Write-Step "Resolving latest release from https://github.com/$Repo"
$releaseUri = "https://api.github.com/repos/$Repo/releases/latest"
try {
  $release = Get-GitHubJson -Uri $releaseUri
} catch {
  throw "Could not read the latest GitHub release for $Repo. Confirm the repo has a published release. GitHub error: $($_.Exception.Message)"
}

$asset = $release.assets | Where-Object { $_.name -eq $AssetName } | Select-Object -First 1
if (-not $asset) {
  $available = ($release.assets | ForEach-Object name) -join ', '
  if (-not $available) { $available = 'none' }
  throw "Latest release '$($release.tag_name)' does not contain $AssetName. Available assets: $available"
}

$tempDir = Join-Path ([System.IO.Path]::GetTempPath()) ("PcSetupMaintainerInstall-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $tempDir | Out-Null

try {
  $downloadPath = Join-Path $tempDir $AssetName
  Write-Step "Downloading $AssetName from release $($release.tag_name)"
  Invoke-WebRequest -Uri $asset.browser_download_url -OutFile $downloadPath

  Write-Step 'Verifying SHA-256 checksum'
  $expectedHash = Get-ExpectedSha256 -Release $release -Asset $asset -TempDir $tempDir
  $actualHash = (Get-FileHash -LiteralPath $downloadPath -Algorithm SHA256).Hash.ToLowerInvariant()
  if ($actualHash -ne $expectedHash) {
    throw "Checksum mismatch. Expected $expectedHash but downloaded $actualHash."
  }

  Write-Step "Installing to $InstallDir"
  New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null
  $installedExe = Join-Path $InstallDir $AssetName
  if (Test-Path -LiteralPath $installedExe) {
    $backup = Join-Path $InstallDir ("PcSetupMaintainer.previous-" + (Get-Date -Format 'yyyyMMddHHmmss') + '.exe')
    Move-Item -LiteralPath $installedExe -Destination $backup -Force
  }
  Copy-Item -LiteralPath $downloadPath -Destination $installedExe -Force

  $metadata = [pscustomobject]@{
    repository = $Repo
    tag = $release.tag_name
    source = $asset.browser_download_url
    sha256 = $actualHash
    installedAt = (Get-Date).ToUniversalTime().ToString('o')
  } | ConvertTo-Json -Depth 4
  Set-Content -LiteralPath (Join-Path $InstallDir 'install-metadata.json') -Value $metadata -Encoding UTF8

  Write-Step 'Creating shortcuts'
  $desktopShortcut = Join-Path ([Environment]::GetFolderPath('DesktopDirectory')) 'PC Setup Maintainer.lnk'
  $startMenuDir = Join-Path ([Environment]::GetFolderPath('Programs')) 'PC Setup Maintainer'
  New-Item -ItemType Directory -Force -Path $startMenuDir | Out-Null
  $startMenuShortcut = Join-Path $startMenuDir 'PC Setup Maintainer.lnk'
  New-Shortcut -Path $desktopShortcut -TargetPath $installedExe -WorkingDirectory $InstallDir
  New-Shortcut -Path $startMenuShortcut -TargetPath $installedExe -WorkingDirectory $InstallDir

  Write-Step "Installed $AssetName $($release.tag_name)"
  Write-Step "Verified SHA-256: $actualHash"

  if (-not $NoLaunch) {
    Write-Step 'Launching PC Setup Maintainer'
    Start-Process -FilePath $installedExe -Verb RunAs
  }
} finally {
  if (Test-Path -LiteralPath $tempDir) {
    Remove-Item -LiteralPath $tempDir -Recurse -Force -ErrorAction SilentlyContinue
  }
}
