param(
  [ValidateSet('Debug', 'Release')]
  [string]$Configuration = 'Release',

  [ValidateSet('win-x64', 'win-arm64')]
  [string]$Runtime = 'win-x64',

  [switch]$SelfContained
)

$ErrorActionPreference = 'Stop'
$RepoRoot = Split-Path -Parent $PSScriptRoot
$Project = Join-Path $RepoRoot 'src\PcSetupMaintainer\PcSetupMaintainer.csproj'
$Output = Join-Path $RepoRoot "artifacts\app\$Runtime"

if (-not (Get-Command dotnet.exe -ErrorAction SilentlyContinue)) {
  throw 'dotnet.exe was not found. Install the .NET 8 SDK.'
}

$Sdks = dotnet --list-sdks
if (-not $Sdks) {
  throw 'No .NET SDK was found. Install Microsoft.DotNet.SDK.8.'
}

New-Item -ItemType Directory -Force -Path $Output | Out-Null

$selfContainedValue = if ($SelfContained) { 'true' } else { 'false' }
dotnet publish $Project `
  --configuration $Configuration `
  --runtime $Runtime `
  --output $Output `
  -p:PublishSingleFile=true `
  -p:SelfContained=$selfContainedValue `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true

$Exe = Join-Path $Output 'PcSetupMaintainer.exe'
if (Test-Path -LiteralPath $Exe) {
  $Hash = (Get-FileHash -LiteralPath $Exe -Algorithm SHA256).Hash.ToLowerInvariant()
  Set-Content -LiteralPath (Join-Path $Output 'PcSetupMaintainer.exe.sha256') -Value "$Hash  PcSetupMaintainer.exe" -Encoding ASCII
  Write-Host "SHA256: $Hash"
}

Write-Host "Published to $Output"
