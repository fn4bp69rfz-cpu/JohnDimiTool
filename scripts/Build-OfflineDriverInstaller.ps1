param(
  [Parameter(Mandatory)]
  [string]$PackageRoot,

  [ValidateSet('win-x64', 'win-arm64')]
  [string]$Runtime = 'win-x64',

  [switch]$FrameworkDependent
)

$ErrorActionPreference = 'Stop'
$PackageRoot = (Resolve-Path -LiteralPath $PackageRoot).Path
$SetupScript = Join-Path $PackageRoot 'Setup.ps1'

if (-not (Test-Path -LiteralPath $SetupScript)) {
  throw "Setup.ps1 was not found in package root: $PackageRoot"
}

if (-not (Get-Command dotnet.exe -ErrorAction SilentlyContinue)) {
  throw 'dotnet.exe was not found. Install the .NET 8 SDK to build Setup.exe.'
}

$Sdks = dotnet --list-sdks
if (-not $Sdks) {
  throw 'No .NET SDK was found. Install Microsoft.DotNet.SDK.8 to build Setup.exe.'
}

$TempRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("PcSetupMaintainer-SetupBuild-" + [guid]::NewGuid().ToString('N'))
$PayloadZip = Join-Path $TempRoot 'payload.zip'
$ProjectDir = Join-Path $TempRoot 'SetupBootstrapper'
$PublishDir = Join-Path $TempRoot 'publish'

New-Item -ItemType Directory -Force -Path $ProjectDir | Out-Null
Compress-Archive -Path (Join-Path $PackageRoot '*') -DestinationPath $PayloadZip -Force
Copy-Item -LiteralPath $PayloadZip -Destination (Join-Path $ProjectDir 'payload.zip') -Force

@'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0-windows</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <ApplicationManifest>app.manifest</ApplicationManifest>
    <AssemblyName>Setup</AssemblyName>
  </PropertyGroup>
  <ItemGroup>
    <EmbeddedResource Include="payload.zip" />
  </ItemGroup>
</Project>
'@ | Set-Content -LiteralPath (Join-Path $ProjectDir 'Setup.csproj') -Encoding UTF8

@'
<?xml version="1.0" encoding="utf-8"?>
<assembly manifestVersion="1.0" xmlns="urn:schemas-microsoft-com:asm.v1">
  <assemblyIdentity version="1.0.0.0" name="PcSetupMaintainer.OfflineSetup"/>
  <trustInfo xmlns="urn:schemas-microsoft-com:asm.v2">
    <security>
      <requestedPrivileges xmlns="urn:schemas-microsoft-com:asm.v3">
        <requestedExecutionLevel level="requireAdministrator" uiAccess="false" />
      </requestedPrivileges>
    </security>
  </trustInfo>
</assembly>
'@ | Set-Content -LiteralPath (Join-Path $ProjectDir 'app.manifest') -Encoding UTF8

@'
using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;

var extractRoot = Path.Combine(Path.GetTempPath(), "PcSetupMaintainer-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(extractRoot);

var assembly = Assembly.GetExecutingAssembly();
var resourceName = assembly.GetManifestResourceNames().Single(name => name.EndsWith("payload.zip", StringComparison.OrdinalIgnoreCase));
await using (var resource = assembly.GetManifestResourceStream(resourceName) ?? throw new InvalidOperationException("payload.zip missing"))
await using (var file = File.Create(Path.Combine(extractRoot, "payload.zip")))
{
    await resource.CopyToAsync(file);
}

ZipFile.ExtractToDirectory(Path.Combine(extractRoot, "payload.zip"), extractRoot, overwriteFiles: true);
var setupScript = Path.Combine(extractRoot, "Setup.ps1");
if (!File.Exists(setupScript))
{
    throw new FileNotFoundException("Setup.ps1 was not found after extraction.", setupScript);
}

var startInfo = new ProcessStartInfo("powershell.exe",
    $"-NoProfile -ExecutionPolicy Bypass -File \"{setupScript}\"")
{
    UseShellExecute = true,
    Verb = "runas",
    WorkingDirectory = extractRoot
};

using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to launch Setup.ps1");
process.WaitForExit();
return process.ExitCode;
'@ | Set-Content -LiteralPath (Join-Path $ProjectDir 'Program.cs') -Encoding UTF8

$selfContained = if ($FrameworkDependent) { 'false' } else { 'true' }
dotnet publish (Join-Path $ProjectDir 'Setup.csproj') `
  --configuration Release `
  --runtime $Runtime `
  --output $PublishDir `
  -p:PublishSingleFile=true `
  -p:SelfContained=$selfContained `
  -p:EnableCompressionInSingleFile=true

$BuiltExe = Join-Path $PublishDir 'Setup.exe'
$FinalExe = Join-Path $PackageRoot 'Setup.exe'
Copy-Item -LiteralPath $BuiltExe -Destination $FinalExe -Force
Remove-Item -LiteralPath $TempRoot -Recurse -Force

Write-Host "Created $FinalExe"
