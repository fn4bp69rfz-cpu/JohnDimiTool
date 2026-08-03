using System.IO;
using System.Text.Json;
using PcSetupMaintainer.Models;

namespace PcSetupMaintainer.Services;

public sealed class SoftwareService
{
    private readonly ShellRunner _shell;
    private readonly AppLogger _logger;

    public SoftwareService(ShellRunner shell, AppLogger logger)
    {
        _shell = shell;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SoftwareCatalogItem>> LoadCatalogAsync(CancellationToken cancellationToken = default)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Resources", "software-catalog.json");
        if (!File.Exists(path))
        {
            path = Path.Combine(AppContext.BaseDirectory, "software-catalog.json");
        }

        await using var stream = File.OpenRead(path);
        var items = await JsonSerializer.DeserializeAsync<List<SoftwareCatalogItem>>(
            stream,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true },
            cancellationToken);

        return items ?? [];
    }

    public async Task InstallAsync(
        IEnumerable<SoftwareCatalogItem> items,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var selected = items.ToList();
        if (selected.Count == 0)
        {
            _logger.Warn("No software packages selected.");
            return;
        }

        var winget = await _shell.RunAsync("where.exe", "winget", cancellationToken);
        if (!winget.Succeeded)
        {
            throw new InvalidOperationException("winget.exe was not found. Install App Installer from Microsoft Store or use Windows 11 22H2+.");
        }

        for (var i = 0; i < selected.Count; i++)
        {
            var item = selected[i];
            _logger.Info($"Installing {item.Name} ({item.Id})");
            await _shell.RunAsync(
                "winget.exe",
                $"install --id \"{item.Id}\" --exact --silent --accept-package-agreements --accept-source-agreements",
                cancellationToken);
            progress?.Report(((i + 1) / (double)selected.Count) * 100);
        }
    }

    public async Task DownloadAsync(
        IEnumerable<SoftwareCatalogItem> items,
        string downloadDirectory,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var selected = items.ToList();
        if (selected.Count == 0)
        {
            _logger.Warn("No software packages selected for download.");
            return;
        }

        Directory.CreateDirectory(downloadDirectory);

        var winget = await _shell.RunAsync("where.exe", "winget", cancellationToken);
        if (!winget.Succeeded)
        {
            throw new InvalidOperationException("winget.exe was not found. Install App Installer from Microsoft Store or use Windows 11 22H2+.");
        }

        for (var i = 0; i < selected.Count; i++)
        {
            var item = selected[i];
            _logger.Info($"Downloading installer for {item.Name} ({item.Id})");
            await _shell.RunAsync(
                "winget.exe",
                $"download --id \"{item.Id}\" --exact --download-directory \"{downloadDirectory}\" --accept-package-agreements --accept-source-agreements",
                cancellationToken);
            progress?.Report(((i + 1) / (double)selected.Count) * 100);
        }
    }

    public async Task ExportInstallScriptAsync(
        IEnumerable<SoftwareCatalogItem> items,
        string outputPath,
        CancellationToken cancellationToken = default)
    {
        var selected = items.ToList();
        var lines = new List<string>
        {
            "#Requires -RunAsAdministrator",
            "$ErrorActionPreference = 'Continue'",
            "if (-not (Get-Command winget.exe -ErrorAction SilentlyContinue)) { throw 'winget.exe was not found.' }"
        };

        lines.AddRange(selected.Select(item =>
            $"winget install --id \"{item.Id}\" --exact --silent --accept-package-agreements --accept-source-agreements"));

        await File.WriteAllLinesAsync(outputPath, lines, cancellationToken);
        _logger.Info($"Software install script written to {outputPath}");
    }
}
