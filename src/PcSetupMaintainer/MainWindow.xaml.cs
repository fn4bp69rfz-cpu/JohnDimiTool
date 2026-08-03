using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Data;
using Microsoft.Win32;
using PcSetupMaintainer.Models;
using PcSetupMaintainer.Services;

namespace PcSetupMaintainer;

public partial class MainWindow : Window
{
    private readonly AppLogger _logger = new();
    private readonly ShellRunner _shell;
    private readonly DriverService _driverService;
    private readonly SoftwareService _softwareService;
    private readonly TweakService _tweakService;
    private readonly ObservableCollection<DriverUpdateItem> _driverUpdates = new();
    private readonly ObservableCollection<SoftwareCatalogItem> _software = new();
    private readonly ObservableCollection<TweakItem> _tweaks = new();
    private ICollectionView? _softwareView;

    public MainWindow()
    {
        InitializeComponent();
        _shell = new ShellRunner(_logger);
        _driverService = new DriverService(_shell, _logger);
        _softwareService = new SoftwareService(_shell, _logger);
        _tweakService = new TweakService(_shell, _logger);

        LogGrid.ItemsSource = _logger.Entries;
        DriverDownloadPathBox.Text = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            "PcSetupMaintainer-DriverDownloads");
        SoftwareDownloadPathBox.Text = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            "PcSetupMaintainer-AppDownloads");
        DriverUpdatesGrid.ItemsSource = _driverUpdates;

        Loaded += MainWindow_Loaded;
        _logger.Info("Application started.");
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            foreach (var item in await _softwareService.LoadCatalogAsync())
            {
                _software.Add(item);
            }

            SoftwareGrid.ItemsSource = _software;
            _softwareView = CollectionViewSource.GetDefaultView(SoftwareGrid.ItemsSource);
            _softwareView.Filter = FilterSoftware;

            foreach (var tweak in _tweakService.GetTweaks())
            {
                _tweaks.Add(tweak);
            }

            TweaksGrid.ItemsSource = _tweaks;
            StatusText.Text = "Ready";
        }
        catch (Exception ex)
        {
            _logger.Error(ex.Message);
            MessageBox.Show(ex.Message, "Startup failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void ScanDriverUpdates_Click(object sender, RoutedEventArgs e)
    {
        await RunWithUiStateAsync("Scanning online driver updates", async progress =>
        {
            _driverUpdates.Clear();
            var hardware = await _driverService.DetectHardwareAsync();
            HardwareText.Text =
                $"{hardware.Manufacturer} {hardware.Model} | Board: {hardware.BaseBoard} | BIOS: {hardware.BiosVersion} | Windows: {hardware.WindowsVersion}";

            var updates = await _driverService.ScanOnlineUpdatesAsync(progress);
            foreach (var update in updates)
            {
                _driverUpdates.Add(update);
            }
        });
    }

    private async void DownloadDriverUpdates_Click(object sender, RoutedEventArgs e)
    {
        await RunWithUiStateAsync("Downloading selected driver updates", progress =>
            _driverService.DownloadOnlineUpdatesAsync(
                _driverUpdates.Where(x => x.IsSelected),
                DriverDownloadPathBox.Text,
                progress));
    }

    private async void InstallDriverUpdates_Click(object sender, RoutedEventArgs e)
    {
        var firmwareSelected = _driverUpdates.Any(x => x.IsSelected && x.IsFirmwareOrBios);
        if (firmwareSelected && IncludeFirmwareCheckBox.IsChecked != true)
        {
            MessageBox.Show(
                "BIOS/Firmware items are selected but the BIOS/Firmware opt-in checkbox is not enabled. They will not be installed.",
                "BIOS/Firmware opt-in required",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        if (IncludeFirmwareCheckBox.IsChecked == true)
        {
            var answer = MessageBox.Show(
                "BIOS and firmware updates can require AC power, BitLocker suspension, and reboot. Continue only if this PC is ready.",
                "Confirm BIOS/Firmware updates",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (answer != MessageBoxResult.Yes) return;
        }

        await RunWithUiStateAsync("Installing selected driver updates", progress =>
            _driverService.InstallOnlineUpdatesAsync(
                _driverUpdates.Where(x => x.IsSelected),
                IncludeFirmwareCheckBox.IsChecked == true,
                progress));
    }

    private async void InstallSoftware_Click(object sender, RoutedEventArgs e)
    {
        await RunWithUiStateAsync("Installing software", progress =>
            _softwareService.InstallAsync(_software.Where(x => x.IsSelected), progress));
    }

    private async void DownloadSoftware_Click(object sender, RoutedEventArgs e)
    {
        await RunWithUiStateAsync("Downloading software installers", progress =>
            _softwareService.DownloadAsync(
                _software.Where(x => x.IsSelected),
                SoftwareDownloadPathBox.Text,
                progress));
    }

    private async void ExportSoftwareScript_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            FileName = "Install-SelectedSoftware.ps1",
            Filter = "PowerShell script (*.ps1)|*.ps1|All files (*.*)|*.*"
        };

        if (dialog.ShowDialog(this) != true) return;

        await RunWithUiStateAsync("Exporting software script", async progress =>
        {
            progress.Report(25);
            await _softwareService.ExportInstallScriptAsync(_software.Where(x => x.IsSelected), dialog.FileName);
            progress.Report(100);
        });
    }

    private async void ApplyTweaks_Click(object sender, RoutedEventArgs e)
    {
        var advanced = _tweaks.Any(t => t.IsSelected && t.IsAdvanced);
        if (advanced)
        {
            var answer = MessageBox.Show(
                "Advanced tweaks can change power behavior or user experience. Continue?",
                "Confirm advanced tweaks",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (answer != MessageBoxResult.Yes) return;
        }

        await RunWithUiStateAsync("Applying tweaks", progress =>
            _tweakService.ApplyAsync(_tweaks.Where(x => x.IsSelected), progress));
    }

    private void SoftwareSearchBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e) =>
        _softwareView?.Refresh();

    private void SelectVisibleSoftware_Click(object sender, RoutedEventArgs e)
    {
        if (_softwareView is null) return;
        foreach (SoftwareCatalogItem item in _softwareView)
        {
            item.IsSelected = true;
        }
    }

    private void ClearSoftware_Click(object sender, RoutedEventArgs e)
    {
        foreach (var item in _software)
        {
            item.IsSelected = false;
        }
    }

    private void OpenDriverDownloadFolder_Click(object sender, RoutedEventArgs e)
    {
        OpenFolder(DriverDownloadPathBox.Text);
    }

    private void OpenSoftwareDownloadFolder_Click(object sender, RoutedEventArgs e)
    {
        OpenFolder(SoftwareDownloadPathBox.Text);
    }

    private static void OpenFolder(string path)
    {
        if (!Directory.Exists(path))
        {
            Directory.CreateDirectory(path);
        }

        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
    }

    private void OpenLog_Click(object sender, RoutedEventArgs e)
    {
        Process.Start(new ProcessStartInfo("notepad.exe", $"\"{_logger.LogPath}\"") { UseShellExecute = true });
    }

    private bool FilterSoftware(object obj)
    {
        if (obj is not SoftwareCatalogItem item) return false;
        var query = SoftwareSearchBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(query)) return true;

        return item.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
               || item.Category.Contains(query, StringComparison.OrdinalIgnoreCase)
               || item.Id.Contains(query, StringComparison.OrdinalIgnoreCase)
               || item.Tags.Contains(query, StringComparison.OrdinalIgnoreCase)
               || item.Description.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    private async Task RunWithUiStateAsync(string status, Func<IProgress<double>, Task> operation)
    {
        try
        {
            IsEnabled = false;
            StatusText.Text = status;
            ProgressBar.Value = 0;
            var progress = new Progress<double>(value => ProgressBar.Value = value);
            await operation(progress);
            StatusText.Text = "Done";
        }
        catch (Exception ex)
        {
            _logger.Error(ex.ToString());
            StatusText.Text = "Failed";
            MessageBox.Show(ex.Message, status, MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsEnabled = true;
        }
    }

    private static string FindRepoScript(string name)
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "scripts", name),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "scripts", name),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "scripts", name)
        };

        foreach (var candidate in candidates.Select(Path.GetFullPath))
        {
            if (File.Exists(candidate)) return candidate;
        }

        throw new FileNotFoundException($"Could not locate {name} near the application.");
    }
}
