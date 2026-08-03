# PC Setup Maintainer

A Windows desktop application for PC setup and maintenance:

- driver discovery and offline package creation;
- exported current-system driver payloads via `pnputil`;
- OEM/GPU/chipset update utility planning through supported vendor tools;
- searchable `winget`-backed software catalog;
- safe Windows cleanup, privacy, power, gaming, and startup-management tweaks;
- structured logging and progress reporting;
- publish script and one-line PowerShell installer bootstrap.

## Build

Install the .NET 8 SDK, then run:

```powershell
.\scripts\Build-App.ps1 -Configuration Release -Runtime win-x64 -SelfContained
```

Output is written to `artifacts\app\win-x64`.

## Create an offline driver package

Run the app as Administrator and use the Drivers tab. It creates:

- `drivers\exported-current-system` with `pnputil` exported drivers;
- `metadata\hardware.json`;
- `metadata\driver-plan.json`;
- `Setup.ps1`.

To turn the package into a single `Setup.exe`:

```powershell
.\scripts\Build-OfflineDriverInstaller.ps1 -PackageRoot .\PcSetupDriverPackage -Runtime win-x64
```

The generated `Setup.exe` embeds the package payload and launches `Setup.ps1` as Administrator on the target PC.

## One-line install

The public installer command is:

```powershell
irm https://raw.githubusercontent.com/fn4bp69rfz-cpu/JohnDimiTool/main/scripts/Install-Latest.ps1 | iex
```

The script downloads the latest GitHub release asset named `PcSetupMaintainer.exe`, verifies its SHA-256 checksum from GitHub release metadata or a sidecar `PcSetupMaintainer.exe.sha256` asset, installs it to `%LOCALAPPDATA%\Programs\PcSetupMaintainer`, creates Desktop and Start Menu shortcuts, and launches the app as Administrator.

Each release must include:

```powershell
PcSetupMaintainer.exe
PcSetupMaintainer.exe.sha256
```

## Driver support boundary

Driver packaging can reliably export already-installed Plug and Play drivers. BIOS and firmware updates are vendor-specific and are only automated through supported manufacturer utilities. The app intentionally does not force silent BIOS flashing because that can brick hardware if prerequisites such as AC power, BitLocker suspension, model matching, or reboot handling are wrong.
