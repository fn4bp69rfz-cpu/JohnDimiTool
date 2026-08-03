# PC Setup Maintainer

A Windows desktop application for PC setup and maintenance:

- online driver discovery through Windows Update;
- selected or automatic driver download/install through Windows Update;
- OEM/GPU/chipset update flows for supported Dell, Lenovo, HP, NVIDIA, AMD, and Intel updater tools;
- BIOS and firmware update support through vendor-supported tools only, with explicit opt-in;
- searchable `winget`-backed software catalog with in-app download and install actions;
- prebuilt Windows cleanup, privacy, power, gaming, network, and startup-management tweaks;
- structured logging and progress reporting;
- publish script and one-line PowerShell installer bootstrap.

## Build

Install the .NET 8 SDK, then run:

```powershell
.\scripts\Build-App.ps1 -Configuration Release -Runtime win-x64 -SelfContained
```

Output is written to `artifacts\app\win-x64`.

## Driver, BIOS, and firmware updates

Run the app as Administrator and use the Drivers tab:

- `Scan Online` detects hardware and searches Windows Update for available driver packages.
- OEM/GPU/chipset rows appear when supported hardware is detected.
- `Download Selected` downloads or prepares selected update packages where the provider supports it.
- `Install Selected` installs selected updates.
- BIOS/Firmware rows are skipped unless `Allow BIOS/Firmware installs` is explicitly enabled.

BIOS and firmware updates intentionally use Dell/Lenovo/HP supported updater tools where available. The app does not scrape random BIOS files or force blind flashing.

## Software downloader/installer

Use the Software tab to search the built-in catalog, select apps, then either:

- `Download Selected` to save installers into the chosen download folder;
- `Install Selected` to download/install the apps through `winget`;
- `Export Script` to generate a reusable PowerShell install script.

## PC tweaks

The PC Tweaks tab contains prebuilt selectable actions. Safe defaults are selected automatically; advanced options require confirmation.

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

Windows Update driver packages can be discovered/downloaded/installed directly. BIOS and firmware updates are vendor-specific and are automated only through supported manufacturer utilities. The app intentionally does not force silent BIOS flashing because that can brick hardware if prerequisites such as AC power, BitLocker suspension, model matching, or reboot handling are wrong.
