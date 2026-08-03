using System.IO;
using System.Text.Json;
using PcSetupMaintainer.Models;

namespace PcSetupMaintainer.Services;

public sealed class DriverService
{
    private readonly ShellRunner _shell;
    private readonly AppLogger _logger;

    public DriverService(ShellRunner shell, AppLogger logger)
    {
        _shell = shell;
        _logger = logger;
    }

    public async Task<DriverPackagePlan> CreatePackageAsync(
        string packageRoot,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(packageRoot);
        Directory.CreateDirectory(Path.Combine(packageRoot, "drivers"));
        Directory.CreateDirectory(Path.Combine(packageRoot, "tools"));
        Directory.CreateDirectory(Path.Combine(packageRoot, "metadata"));

        progress?.Report(5);
        var hardware = await DetectHardwareAsync(cancellationToken);
        progress?.Report(20);

        var sources = BuildDriverSources(hardware);
        var plan = new DriverPackagePlan(
            hardware,
            sources,
            packageRoot,
            Path.Combine(packageRoot, "Setup.ps1"));

        await File.WriteAllTextAsync(
            Path.Combine(packageRoot, "metadata", "hardware.json"),
            JsonSerializer.Serialize(hardware, JsonOptions()),
            cancellationToken);

        await File.WriteAllTextAsync(
            Path.Combine(packageRoot, "metadata", "driver-plan.json"),
            JsonSerializer.Serialize(plan, JsonOptions()),
            cancellationToken);

        progress?.Report(35);
        await ExportInstalledDriversAsync(packageRoot, cancellationToken);
        progress?.Report(65);

        await WriteSetupScriptAsync(plan, cancellationToken);
        await WriteReadmeAsync(plan, cancellationToken);

        progress?.Report(100);
        _logger.Info($"Driver package created at {packageRoot}");
        return plan;
    }

    public async Task<HardwareSummary> DetectHardwareAsync(CancellationToken cancellationToken = default)
    {
        const string script = """
        $ErrorActionPreference = 'SilentlyContinue'
        $computer = Get-CimInstance Win32_ComputerSystem
        $baseBoard = Get-CimInstance Win32_BaseBoard
        $bios = Get-CimInstance Win32_BIOS
        $os = Get-CimInstance Win32_OperatingSystem
        $video = Get-CimInstance Win32_VideoController | Select-Object -ExpandProperty Name
        $network = Get-CimInstance Win32_NetworkAdapter | Where-Object { $_.PhysicalAdapter -eq $true -and $_.Name } | Select-Object -First 12 -ExpandProperty Name
        $audio = Get-CimInstance Win32_PnPEntity | Where-Object { $_.PNPClass -eq 'MEDIA' -and $_.Name } | Select-Object -First 12 -ExpandProperty Name
        [pscustomobject]@{
          Manufacturer = [string]$computer.Manufacturer
          Model = [string]$computer.Model
          BaseBoard = (($baseBoard.Manufacturer, $baseBoard.Product) -join ' ').Trim()
          BiosVersion = (($bios.SMBIOSBIOSVersion, $bios.ReleaseDate) -join ' ').Trim()
          WindowsVersion = (($os.Caption, $os.Version, $os.OSArchitecture) -join ' ').Trim()
          DisplayAdapters = @($video)
          NetworkAdapters = @($network)
          AudioDevices = @($audio)
        } | ConvertTo-Json -Depth 5
        """;

        var result = await _shell.RunPowerShellAsync(script, cancellationToken);
        if (!result.Succeeded || string.IsNullOrWhiteSpace(result.StandardOutput))
        {
            _logger.Warn("Hardware detection returned incomplete data.");
            return new HardwareSummary("Unknown", "Unknown", "Unknown", "Unknown", "Unknown", [], [], []);
        }

        return JsonSerializer.Deserialize<HardwareSummary>(result.StandardOutput, JsonOptions())
               ?? new HardwareSummary("Unknown", "Unknown", "Unknown", "Unknown", "Unknown", [], [], []);
    }

    public async Task<IReadOnlyList<DriverUpdateItem>> ScanOnlineUpdatesAsync(
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        _logger.Info("Scanning hardware and online update sources.");
        progress?.Report(5);
        var hardware = await DetectHardwareAsync(cancellationToken);
        progress?.Report(20);

        var items = new List<DriverUpdateItem>();
        items.AddRange(BuildOnlineUpdateTasks(hardware));
        progress?.Report(35);

        var windowsUpdates = await SearchWindowsUpdateDriversAsync(cancellationToken);
        items.AddRange(windowsUpdates);
        progress?.Report(100);

        if (items.Count == 0)
        {
            _logger.Info("No online driver update tasks were discovered.");
        }

        return items;
    }

    public async Task DownloadOnlineUpdatesAsync(
        IEnumerable<DriverUpdateItem> items,
        string downloadRoot,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var selected = items.ToList();
        if (selected.Count == 0)
        {
            _logger.Warn("No driver update items selected for download.");
            return;
        }

        Directory.CreateDirectory(downloadRoot);
        for (var i = 0; i < selected.Count; i++)
        {
            var item = selected[i];
            item.Status = "Downloading";
            _logger.Info($"Downloading/update-preparing: {item.Name}");

            if (item.Action.StartsWith("windows-update:", StringComparison.OrdinalIgnoreCase))
            {
                await DownloadWindowsUpdateDriversByTitleAsync([item.Name], cancellationToken);
            }
            else
            {
                await RunVendorActionAsync(item, install: false, downloadRoot, cancellationToken);
            }

            item.Status = "Downloaded/prepared";
            progress?.Report(((i + 1) / (double)selected.Count) * 100);
        }
    }

    public async Task InstallOnlineUpdatesAsync(
        IEnumerable<DriverUpdateItem> items,
        bool includeFirmwareAndBios,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var selected = items.Where(item => includeFirmwareAndBios || !item.IsFirmwareOrBios).ToList();
        if (selected.Count == 0)
        {
            _logger.Warn("No driver update items selected for install.");
            return;
        }

        var windowsTitles = selected
            .Where(item => item.Action.StartsWith("windows-update:", StringComparison.OrdinalIgnoreCase))
            .Select(item => item.Name)
            .ToList();

        if (windowsTitles.Count > 0)
        {
            foreach (var item in selected.Where(item => windowsTitles.Contains(item.Name)))
            {
                item.Status = "Installing";
            }

            await InstallWindowsUpdateDriversByTitleAsync(windowsTitles, cancellationToken);

            foreach (var item in selected.Where(item => windowsTitles.Contains(item.Name)))
            {
                item.Status = "Installed or queued";
            }
        }

        var vendorItems = selected
            .Where(item => !item.Action.StartsWith("windows-update:", StringComparison.OrdinalIgnoreCase))
            .ToList();

        for (var i = 0; i < vendorItems.Count; i++)
        {
            var item = vendorItems[i];
            item.Status = "Installing/running";
            await RunVendorActionAsync(item, install: true, null, cancellationToken);
            item.Status = "Completed or launched";
            progress?.Report(((i + 1) / (double)Math.Max(vendorItems.Count, 1)) * 100);
        }

        progress?.Report(100);
    }

    private async Task<IReadOnlyList<DriverUpdateItem>> SearchWindowsUpdateDriversAsync(CancellationToken cancellationToken)
    {
        const string script = """
        $ErrorActionPreference = 'Stop'
        $session = New-Object -ComObject Microsoft.Update.Session
        $searcher = $session.CreateUpdateSearcher()
        $result = $searcher.Search("IsInstalled=0 and IsHidden=0 and Type='Driver'")
        $items = @()
        for ($i = 0; $i -lt $result.Updates.Count; $i++) {
          $u = $result.Updates.Item($i)
          $categoryNames = @($u.Categories | ForEach-Object { $_.Name })
          $isFirmware = ($u.Title -match '(?i)bios|firmware|uefi') -or (($categoryNames -join ' ') -match '(?i)firmware')
          $items += [pscustomobject]@{
            Id = 'wu-' + $i
            Name = [string]$u.Title
            Category = if ($isFirmware) { 'BIOS/Firmware' } else { 'Driver' }
            Provider = 'Microsoft / Windows Update'
            Source = 'Windows Update online catalog'
            Action = 'windows-update:' + $i
            Version = ''
            Notes = (($categoryNames -join ', ') + ' | ' + [string]$u.Description).Trim(' |')
            IsFirmwareOrBios = [bool]$isFirmware
            IsSelected = $true
            Status = 'Available'
          }
        }
        $items | ConvertTo-Json -Depth 6
        """;

        var result = await _shell.RunPowerShellAsync(script, cancellationToken);
        if (!result.Succeeded || string.IsNullOrWhiteSpace(result.StandardOutput))
        {
            _logger.Warn("Windows Update driver search did not return updates.");
            return [];
        }

        try
        {
            var trimmed = result.StandardOutput.Trim();
            if (!trimmed.StartsWith('['))
            {
                trimmed = "[" + trimmed + "]";
            }

            return JsonSerializer.Deserialize<List<DriverUpdateItem>>(trimmed, JsonOptions()) ?? [];
        }
        catch (Exception ex)
        {
            _logger.Warn($"Could not parse Windows Update driver search results: {ex.Message}");
            return [];
        }
    }

    private async Task DownloadWindowsUpdateDriversByTitleAsync(
        IReadOnlyList<string> titles,
        CancellationToken cancellationToken)
    {
        var titlesJson = JsonSerializer.Serialize(titles);
        var script = $$"""
        $ErrorActionPreference = 'Stop'
        $titles = '{{titlesJson.Replace("'", "''")}}' | ConvertFrom-Json
        $session = New-Object -ComObject Microsoft.Update.Session
        $searcher = $session.CreateUpdateSearcher()
        $result = $searcher.Search("IsInstalled=0 and IsHidden=0 and Type='Driver'")
        $updates = New-Object -ComObject Microsoft.Update.UpdateColl
        for ($i = 0; $i -lt $result.Updates.Count; $i++) {
          $u = $result.Updates.Item($i)
          if ($titles -contains $u.Title) { [void]$updates.Add($u) }
        }
        if ($updates.Count -eq 0) { throw 'No selected Windows Update driver items were found during download.' }
        $downloader = $session.CreateUpdateDownloader()
        $downloader.Updates = $updates
        $downloadResult = $downloader.Download()
        "Downloaded $($updates.Count) Windows Update driver item(s). Result: $($downloadResult.ResultCode)"
        """;

        await _shell.RunPowerShellAsync(script, cancellationToken);
    }

    private async Task InstallWindowsUpdateDriversByTitleAsync(
        IReadOnlyList<string> titles,
        CancellationToken cancellationToken)
    {
        var titlesJson = JsonSerializer.Serialize(titles);
        var script = $$"""
        $ErrorActionPreference = 'Stop'
        $titles = '{{titlesJson.Replace("'", "''")}}' | ConvertFrom-Json
        $session = New-Object -ComObject Microsoft.Update.Session
        $searcher = $session.CreateUpdateSearcher()
        $result = $searcher.Search("IsInstalled=0 and IsHidden=0 and Type='Driver'")
        $updates = New-Object -ComObject Microsoft.Update.UpdateColl
        for ($i = 0; $i -lt $result.Updates.Count; $i++) {
          $u = $result.Updates.Item($i)
          if ($titles -contains $u.Title) { [void]$updates.Add($u) }
        }
        if ($updates.Count -eq 0) { throw 'No selected Windows Update driver items were found during install.' }
        $downloader = $session.CreateUpdateDownloader()
        $downloader.Updates = $updates
        [void]$downloader.Download()
        $installer = $session.CreateUpdateInstaller()
        $installer.Updates = $updates
        $installResult = $installer.Install()
        "Installed/queued $($updates.Count) Windows Update driver item(s). Result: $($installResult.ResultCode). Reboot required: $($installResult.RebootRequired)"
        """;

        await _shell.RunPowerShellAsync(script, cancellationToken);
    }

    private static IReadOnlyList<DriverUpdateItem> BuildOnlineUpdateTasks(HardwareSummary hardware)
    {
        var manufacturer = hardware.Manufacturer.ToLowerInvariant();
        var display = string.Join(' ', hardware.DisplayAdapters).ToLowerInvariant();
        var network = string.Join(' ', hardware.NetworkAdapters).ToLowerInvariant();
        var tasks = new List<DriverUpdateItem>();

        if (manufacturer.Contains("dell"))
        {
            tasks.Add(new()
            {
                Id = "vendor-dell-command-update",
                Name = "Dell driver, BIOS, and firmware updates",
                Category = "OEM + BIOS/Firmware",
                Provider = "Dell",
                Source = "Dell Command Update online service",
                Action = "vendor:dell-command-update",
                IsFirmwareOrBios = true,
                IsSelected = true,
                Notes = "Downloads and installs current Dell model-matched updates using Dell's supported updater."
            });
        }
        else if (manufacturer.Contains("lenovo"))
        {
            tasks.Add(new()
            {
                Id = "vendor-lenovo-system-update",
                Name = "Lenovo driver, BIOS, and firmware updates",
                Category = "OEM + BIOS/Firmware",
                Provider = "Lenovo",
                Source = "Lenovo System Update online service",
                Action = "vendor:lenovo-system-update",
                IsFirmwareOrBios = true,
                IsSelected = true,
                Notes = "Downloads and installs current Lenovo model-matched updates using Lenovo's supported updater."
            });
        }
        else if (manufacturer.Contains("hp") || manufacturer.Contains("hewlett"))
        {
            tasks.Add(new()
            {
                Id = "vendor-hp-image-assistant",
                Name = "HP driver, BIOS, and firmware updates",
                Category = "OEM + BIOS/Firmware",
                Provider = "HP",
                Source = "HP Image Assistant online service",
                Action = "vendor:hp-image-assistant",
                IsFirmwareOrBios = true,
                IsSelected = true,
                Notes = "Downloads and installs supported HP SoftPaq updates with HP Image Assistant."
            });
        }

        if (display.Contains("nvidia"))
        {
            tasks.Add(new()
            {
                Id = "gpu-nvidia-app",
                Name = "NVIDIA latest graphics driver",
                Category = "GPU",
                Provider = "NVIDIA",
                Source = "NVIDIA App",
                Action = "vendor:nvidia-app",
                IsSelected = true,
                Notes = "Installs/opens NVIDIA App so the current GPU driver can be downloaded and installed."
            });
        }

        if (display.Contains("amd") || display.Contains("radeon"))
        {
            tasks.Add(new()
            {
                Id = "gpu-amd-software",
                Name = "AMD latest graphics/chipset driver",
                Category = "GPU/Chipset",
                Provider = "AMD",
                Source = "AMD Software",
                Action = "vendor:amd-software",
                IsSelected = true,
                Notes = "Installs/opens AMD Software so current GPU/chipset packages can be downloaded and installed."
            });
        }

        if (display.Contains("intel") || network.Contains("intel"))
        {
            tasks.Add(new()
            {
                Id = "intel-driver-support-assistant",
                Name = "Intel chipset, graphics, Wi-Fi, Bluetooth, and Ethernet updates",
                Category = "Chipset/GPU/Network",
                Provider = "Intel",
                Source = "Intel Driver & Support Assistant",
                Action = "vendor:intel-dsa",
                IsSelected = true,
                Notes = "Installs/opens Intel DSA for current Intel component driver downloads."
            });
        }

        tasks.Add(new()
        {
            Id = "windows-update-driver-scan",
            Name = "Windows Update online driver scan",
            Category = "Driver",
            Provider = "Microsoft",
            Source = "Windows Update",
            Action = "vendor:windows-settings",
            IsSelected = false,
            Notes = "Opens Windows Update optional updates if no direct driver items are returned by the API."
        });

        return tasks;
    }

    private async Task RunVendorActionAsync(
        DriverUpdateItem item,
        bool install,
        string? downloadRoot,
        CancellationToken cancellationToken)
    {
        var script = item.Action switch
        {
            "vendor:dell-command-update" => install
                ? """
                  if (Get-Command winget.exe -ErrorAction SilentlyContinue) { winget install --id Dell.CommandUpdate --exact --silent --accept-package-agreements --accept-source-agreements }
                  $dcu = Get-ChildItem -Path "$env:ProgramFiles\Dell\CommandUpdate","${env:ProgramFiles(x86)}\Dell\CommandUpdate" -Filter dcu-cli.exe -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1
                  if (-not $dcu) { throw 'Dell Command Update CLI was not found after install.' }
                  & $dcu.FullName /scan -silent
                  & $dcu.FullName /applyUpdates -silent -reboot=disable
                  """
                : "winget download --id Dell.CommandUpdate --exact --download-directory \"{0}\" --accept-package-agreements --accept-source-agreements",
            "vendor:lenovo-system-update" => install
                ? """
                  if (Get-Command winget.exe -ErrorAction SilentlyContinue) { winget install --id Lenovo.SystemUpdate --exact --silent --accept-package-agreements --accept-source-agreements }
                  $tvsu = Get-ChildItem -Path "$env:ProgramFiles\Lenovo","${env:ProgramFiles(x86)}\Lenovo" -Filter tvsu.exe -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1
                  if (-not $tvsu) { throw 'Lenovo System Update CLI was not found after install.' }
                  & $tvsu.FullName /CM -search A -action INSTALL -includerebootpackages 1,3,4 -noicon
                  """
                : "winget download --id Lenovo.SystemUpdate --exact --download-directory \"{0}\" --accept-package-agreements --accept-source-agreements",
            "vendor:hp-image-assistant" => install
                ? """
                  if (Get-Command winget.exe -ErrorAction SilentlyContinue) { winget install --id HP.HPImageAssistant --exact --silent --accept-package-agreements --accept-source-agreements }
                  $hia = Get-ChildItem -Path "$env:ProgramFiles","${env:ProgramFiles(x86)}" -Filter HPImageAssistant.exe -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1
                  if (-not $hia) { throw 'HP Image Assistant was not found after install.' }
                  & $hia.FullName /Operation:Analyze /Action:Install /Silent /SoftpaqDownloadFolder:"$env:ProgramData\PcSetupMaintainer\HP"
                  """
                : "winget download --id HP.HPImageAssistant --exact --download-directory \"{0}\" --accept-package-agreements --accept-source-agreements",
            "vendor:nvidia-app" => install
                ? "winget install --id Nvidia.NVIDIAApp --exact --silent --accept-package-agreements --accept-source-agreements; Start-Process 'nvidia-smi.exe' -ErrorAction SilentlyContinue"
                : "winget download --id Nvidia.NVIDIAApp --exact --download-directory \"{0}\" --accept-package-agreements --accept-source-agreements",
            "vendor:amd-software" => install
                ? "winget install --id AdvancedMicroDevices.AMDRadeonSoftware --exact --silent --accept-package-agreements --accept-source-agreements"
                : "winget download --id AdvancedMicroDevices.AMDRadeonSoftware --exact --download-directory \"{0}\" --accept-package-agreements --accept-source-agreements",
            "vendor:intel-dsa" => install
                ? "winget install --id Intel.IntelDriverAndSupportAssistant --exact --silent --accept-package-agreements --accept-source-agreements; Start-Process 'https://www.intel.com/content/www/us/en/support/detect.html'"
                : "winget download --id Intel.IntelDriverAndSupportAssistant --exact --download-directory \"{0}\" --accept-package-agreements --accept-source-agreements",
            "vendor:windows-settings" => "Start-Process 'ms-settings:windowsupdate-optionalupdates'",
            _ => throw new InvalidOperationException($"Unknown online driver update action: {item.Action}")
        };

        if (!install && script.Contains("{0}", StringComparison.Ordinal))
        {
            var safeRoot = downloadRoot ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                "PcSetupMaintainer-DriverDownloads");
            Directory.CreateDirectory(safeRoot);
            script = string.Format(script, safeRoot.Replace("\"", "\"\""));
        }

        await _shell.RunPowerShellAsync(script, cancellationToken);
    }

    private async Task ExportInstalledDriversAsync(string packageRoot, CancellationToken cancellationToken)
    {
        var driverDir = Path.Combine(packageRoot, "drivers", "exported-current-system");
        Directory.CreateDirectory(driverDir);

        var result = await _shell.RunAsync("pnputil.exe", $"/export-driver * \"{driverDir}\"", cancellationToken);
        if (!result.Succeeded)
        {
            _logger.Warn("Driver export failed. The package will still include vendor update instructions.");
        }
    }

    private static IReadOnlyList<DriverSource> BuildDriverSources(HardwareSummary hardware)
    {
        var manufacturer = hardware.Manufacturer.ToLowerInvariant();
        var display = string.Join(' ', hardware.DisplayAdapters).ToLowerInvariant();
        var network = string.Join(' ', hardware.NetworkAdapters).ToLowerInvariant();
        var sources = new List<DriverSource>();

        if (manufacturer.Contains("dell"))
        {
            sources.Add(new("OEM", "Dell Command Update", "Dell", "winget install --id Dell.CommandUpdate --silent", "dcu-cli.exe /scan -silent; dcu-cli.exe /applyUpdates -silent -reboot=disable", true, "Best option for Dell driver, BIOS, and firmware updates."));
        }
        else if (manufacturer.Contains("lenovo"))
        {
            sources.Add(new("OEM", "Lenovo System Update", "Lenovo", "winget install --id Lenovo.SystemUpdate --silent", "tvsu.exe /CM -search A -action INSTALL -includerebootpackages 1,3,4 -noicon", true, "Supports Lenovo driver, BIOS, and firmware updates on supported models."));
        }
        else if (manufacturer.Contains("hp") || manufacturer.Contains("hewlett"))
        {
            sources.Add(new("OEM", "HP Image Assistant", "HP", "winget install --id HP.HPImageAssistant --silent", "HPImageAssistant.exe /Operation:Analyze /Action:Install /Silent /SoftpaqDownloadFolder:tools\\hp", true, "Supports many HP commercial systems."));
        }
        else
        {
            sources.Add(new("OEM", "Manufacturer support page", hardware.Manufacturer, "Manual model lookup", "https://www.google.com/search?q=" + Uri.EscapeDataString($"{hardware.Manufacturer} {hardware.Model} drivers BIOS"), false, "No known generic vendor CLI was selected. Use the linked support lookup for BIOS and firmware."));
        }

        if (display.Contains("nvidia"))
        {
            sources.Add(new("GPU", "NVIDIA App", "NVIDIA", "winget install --id Nvidia.NVIDIAApp --silent", "NVIDIA App driver update flow", false, "NVIDIA requires vendor app or direct model-specific package selection."));
        }
        if (display.Contains("amd") || display.Contains("radeon"))
        {
            sources.Add(new("GPU", "AMD Software", "AMD", "winget install --id AdvancedMicroDevices.AMDRadeonSoftware --silent", "AMD Software driver update flow", false, "AMD GPU packages vary by GPU family."));
        }
        if (display.Contains("intel") || network.Contains("intel"))
        {
            sources.Add(new("Chipset/Network/GPU", "Intel Driver & Support Assistant", "Intel", "winget install --id Intel.IntelDriverAndSupportAssistant --silent", "Intel DSA scan and update flow", false, "Useful for Intel chipset, graphics, Wi-Fi, Bluetooth, and Ethernet components."));
        }

        sources.Add(new("Windows", "Windows Update drivers", "Microsoft", "Built in", "UsoClient StartInteractiveScan", false, "Fallback source for WHQL drivers."));
        return sources;
    }

    private static async Task WriteSetupScriptAsync(DriverPackagePlan plan, CancellationToken cancellationToken)
    {
        var script = $$"""
        #Requires -RunAsAdministrator
        $ErrorActionPreference = 'Continue'
        $Root = Split-Path -Parent $MyInvocation.MyCommand.Path
        $Log = Join-Path $Root 'setup.log'
        function Write-Step($Message) {
          $line = "[{0}] {1}" -f (Get-Date -Format o), $Message
          Write-Host $line
          Add-Content -Path $Log -Value $line
        }

        Write-Step 'Starting PC setup driver package.'
        $DriverRoot = Join-Path $Root 'drivers\exported-current-system'
        if (Test-Path $DriverRoot) {
          Write-Step "Installing exported drivers from $DriverRoot"
          pnputil.exe /add-driver "$DriverRoot\*.inf" /subdirs /install | Tee-Object -FilePath $Log -Append
        }

        if (Get-Command winget.exe -ErrorAction SilentlyContinue) {
          Write-Step 'Installing supported vendor driver utilities.'
        {{string.Join(Environment.NewLine, plan.Sources.Where(s => s.Strategy.StartsWith("winget", StringComparison.OrdinalIgnoreCase)).Select(s => $"  {s.Strategy} --accept-package-agreements --accept-source-agreements | Tee-Object -FilePath $Log -Append"))}}
        } else {
          Write-Step 'winget.exe was not found. Skipping vendor utility installation.'
        }

        Write-Step 'Attempting Windows Update driver scan.'
        UsoClient StartInteractiveScan 2>$null
        Write-Step 'Driver package completed. Reboot if any driver, BIOS, or firmware installer requested it.'
        """;

        await File.WriteAllTextAsync(plan.SetupScriptPath, script, cancellationToken);
    }

    private static async Task WriteReadmeAsync(DriverPackagePlan plan, CancellationToken cancellationToken)
    {
        var readme = $"""
        # Offline PC Driver Package

        Created: {DateTimeOffset.Now:O}
        Manufacturer: {plan.Hardware.Manufacturer}
        Model: {plan.Hardware.Model}
        BIOS: {plan.Hardware.BiosVersion}

        Run `Setup.ps1` as Administrator on the target PC.

        Notes:
        - Exported Plug and Play drivers are included under `drivers/exported-current-system`.
        - OEM BIOS/firmware updates are handled only where the manufacturer exposes a supported update utility.
        - BIOS updates are intentionally not forced silently. Vendor utilities may prompt or require AC power, BitLocker suspension, and reboot.
        """;

        await File.WriteAllTextAsync(Path.Combine(plan.PackageRoot, "README.md"), readme, cancellationToken);
    }

    private static JsonSerializerOptions JsonOptions() => new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
}
