using PcSetupMaintainer.Models;

namespace PcSetupMaintainer.Services;

public sealed class TweakService
{
    private readonly ShellRunner _shell;
    private readonly AppLogger _logger;

    public TweakService(ShellRunner shell, AppLogger logger)
    {
        _shell = shell;
        _logger = logger;
    }

    public IReadOnlyList<TweakItem> GetTweaks() =>
    [
        new() { Id = "power-balanced", Name = "Use Balanced power plan", Category = "Power", Description = "Restores Windows Balanced plan for safe default performance.", IsSelected = true },
        new() { Id = "power-high", Name = "Use High Performance power plan", Category = "Power", Description = "Sets High Performance when available. Useful on desktops and plugged-in workstations." },
        new() { Id = "storage-sense", Name = "Enable Storage Sense", Category = "Cleanup", Description = "Enables Windows automatic cleanup for temporary files." },
        new() { Id = "cleanup-temp", Name = "Clean user temp files", Category = "Cleanup", Description = "Deletes files in the current user's temporary folder that are not locked.", IsSelected = true },
        new() { Id = "privacy-adid", Name = "Disable advertising ID", Category = "Privacy", Description = "Turns off the per-user Windows advertising ID.", IsSelected = true },
        new() { Id = "privacy-tips", Name = "Disable Windows tips and suggestions", Category = "Privacy", Description = "Reduces suggested content and tip notifications." },
        new() { Id = "privacy-diagnostics-basic", Name = "Limit diagnostic data", Category = "Privacy", Description = "Sets diagnostic data collection to the lowest standard Windows setting available.", IsAdvanced = true },
        new() { Id = "privacy-activity-history", Name = "Disable activity history", Category = "Privacy", Description = "Stops Windows from storing activity history for the current user." },
        new() { Id = "gaming-mode", Name = "Enable Game Mode", Category = "Gaming", Description = "Enables Windows Game Mode." },
        new() { Id = "gaming-disable-captures", Name = "Disable background game captures", Category = "Gaming", Description = "Disables background Game DVR recording to reduce overhead." },
        new() { Id = "gaming-hags", Name = "Enable hardware accelerated GPU scheduling", Category = "Gaming", Description = "Enables HAGS where supported. Reboot required.", IsAdvanced = true },
        new() { Id = "dns-flush", Name = "Flush DNS cache", Category = "Network", Description = "Clears local DNS resolver cache." },
        new() { Id = "network-reset-winsock", Name = "Reset Winsock catalog", Category = "Network", Description = "Resets the Winsock network stack. Reboot recommended.", IsAdvanced = true },
        new() { Id = "startup-folder", Name = "Open startup folder", Category = "Startup", Description = "Opens the current user's Startup folder for manual review." },
        new() { Id = "task-manager-startup", Name = "Open Task Manager Startup tab", Category = "Startup", Description = "Opens Startup Apps management. No automatic disabling is performed." },
        new() { Id = "startup-settings", Name = "Open Startup Apps settings", Category = "Startup", Description = "Opens Windows Startup Apps settings so entries can be disabled safely." },
        new() { Id = "visual-effects", Name = "Favor performance visual effects", Category = "Advanced", Description = "Adjusts current-user visual effects preference toward performance.", IsAdvanced = true },
        new() { Id = "hibernate-off", Name = "Disable hibernation", Category = "Advanced", Description = "Disables hibernation and Fast Startup. Frees disk space but removes Hibernate.", IsAdvanced = true },
        new() { Id = "ultimate-performance", Name = "Create Ultimate Performance plan", Category = "Advanced", Description = "Adds and activates Ultimate Performance if Windows supports it.", IsAdvanced = true },
        new() { Id = "component-cleanup", Name = "Run Windows component cleanup", Category = "Cleanup", Description = "Runs DISM StartComponentCleanup to clean superseded Windows components." },
        new() { Id = "recycle-bin", Name = "Empty Recycle Bin", Category = "Cleanup", Description = "Empties Recycle Bin for all drives without prompting.", IsAdvanced = true }
    ];

    public async Task ApplyAsync(
        IEnumerable<TweakItem> tweaks,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var selected = tweaks.ToList();
        if (selected.Count == 0)
        {
            _logger.Warn("No tweaks selected.");
            return;
        }

        for (var i = 0; i < selected.Count; i++)
        {
            var tweak = selected[i];
            _logger.Info($"Applying tweak: {tweak.Name}");
            await ApplyOneAsync(tweak.Id, cancellationToken);
            progress?.Report(((i + 1) / (double)selected.Count) * 100);
        }
    }

    private Task ApplyOneAsync(string id, CancellationToken cancellationToken)
    {
        var script = id switch
        {
            "power-balanced" => "powercfg /setactive SCHEME_BALANCED",
            "power-high" => "powercfg /setactive SCHEME_MIN",
            "storage-sense" => "New-Item -Path 'HKCU:\\Software\\Microsoft\\Windows\\CurrentVersion\\StorageSense\\Parameters\\StoragePolicy' -Force | Out-Null; Set-ItemProperty -Path 'HKCU:\\Software\\Microsoft\\Windows\\CurrentVersion\\StorageSense\\Parameters\\StoragePolicy' -Name '01' -Type DWord -Value 1",
            "cleanup-temp" => "Get-ChildItem -LiteralPath $env:TEMP -Force -ErrorAction SilentlyContinue | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue",
            "privacy-adid" => "New-Item -Path 'HKCU:\\Software\\Microsoft\\Windows\\CurrentVersion\\AdvertisingInfo' -Force | Out-Null; Set-ItemProperty -Path 'HKCU:\\Software\\Microsoft\\Windows\\CurrentVersion\\AdvertisingInfo' -Name Enabled -Type DWord -Value 0",
            "privacy-tips" => "New-Item -Path 'HKCU:\\Software\\Microsoft\\Windows\\CurrentVersion\\ContentDeliveryManager' -Force | Out-Null; Set-ItemProperty -Path 'HKCU:\\Software\\Microsoft\\Windows\\CurrentVersion\\ContentDeliveryManager' -Name SubscribedContent-338389Enabled -Type DWord -Value 0; Set-ItemProperty -Path 'HKCU:\\Software\\Microsoft\\Windows\\CurrentVersion\\ContentDeliveryManager' -Name SystemPaneSuggestionsEnabled -Type DWord -Value 0",
            "privacy-diagnostics-basic" => "New-Item -Path 'HKLM:\\SOFTWARE\\Policies\\Microsoft\\Windows\\DataCollection' -Force | Out-Null; Set-ItemProperty -Path 'HKLM:\\SOFTWARE\\Policies\\Microsoft\\Windows\\DataCollection' -Name AllowTelemetry -Type DWord -Value 1",
            "privacy-activity-history" => "New-Item -Path 'HKLM:\\SOFTWARE\\Policies\\Microsoft\\Windows\\System' -Force | Out-Null; Set-ItemProperty -Path 'HKLM:\\SOFTWARE\\Policies\\Microsoft\\Windows\\System' -Name EnableActivityFeed -Type DWord -Value 0; Set-ItemProperty -Path 'HKLM:\\SOFTWARE\\Policies\\Microsoft\\Windows\\System' -Name PublishUserActivities -Type DWord -Value 0; Set-ItemProperty -Path 'HKLM:\\SOFTWARE\\Policies\\Microsoft\\Windows\\System' -Name UploadUserActivities -Type DWord -Value 0",
            "gaming-mode" => "New-Item -Path 'HKCU:\\Software\\Microsoft\\GameBar' -Force | Out-Null; Set-ItemProperty -Path 'HKCU:\\Software\\Microsoft\\GameBar' -Name AutoGameModeEnabled -Type DWord -Value 1",
            "gaming-disable-captures" => "New-Item -Path 'HKCU:\\System\\GameConfigStore' -Force | Out-Null; Set-ItemProperty -Path 'HKCU:\\System\\GameConfigStore' -Name GameDVR_Enabled -Type DWord -Value 0; New-Item -Path 'HKCU:\\Software\\Microsoft\\Windows\\CurrentVersion\\GameDVR' -Force | Out-Null; Set-ItemProperty -Path 'HKCU:\\Software\\Microsoft\\Windows\\CurrentVersion\\GameDVR' -Name AppCaptureEnabled -Type DWord -Value 0",
            "gaming-hags" => "New-Item -Path 'HKLM:\\SYSTEM\\CurrentControlSet\\Control\\GraphicsDrivers' -Force | Out-Null; Set-ItemProperty -Path 'HKLM:\\SYSTEM\\CurrentControlSet\\Control\\GraphicsDrivers' -Name HwSchMode -Type DWord -Value 2",
            "dns-flush" => "ipconfig /flushdns",
            "network-reset-winsock" => "netsh winsock reset",
            "startup-folder" => "Start-Process shell:startup",
            "task-manager-startup" => "Start-Process taskmgr.exe -ArgumentList '/0 /startup'",
            "startup-settings" => "Start-Process 'ms-settings:startupapps'",
            "visual-effects" => "New-Item -Path 'HKCU:\\Software\\Microsoft\\Windows\\CurrentVersion\\Explorer\\VisualEffects' -Force | Out-Null; Set-ItemProperty -Path 'HKCU:\\Software\\Microsoft\\Windows\\CurrentVersion\\Explorer\\VisualEffects' -Name VisualFXSetting -Type DWord -Value 2",
            "hibernate-off" => "powercfg /hibernate off",
            "ultimate-performance" => "$existing = powercfg /list | Select-String 'Ultimate Performance'; if (-not $existing) { powercfg -duplicatescheme e9a42b02-d5df-448d-aa00-03f14749eb61 | Out-Null }; $guid = (powercfg /list | Select-String 'Ultimate Performance' | Select-Object -First 1).ToString() -replace '.*:\\s*([a-f0-9-]+).*','$1'; if ($guid) { powercfg /setactive $guid }",
            "component-cleanup" => "DISM.exe /Online /Cleanup-Image /StartComponentCleanup",
            "recycle-bin" => "Clear-RecycleBin -Force -ErrorAction SilentlyContinue",
            _ => throw new ArgumentOutOfRangeException(nameof(id), id, "Unknown tweak")
        };

        return _shell.RunPowerShellAsync(script, cancellationToken);
    }
}
