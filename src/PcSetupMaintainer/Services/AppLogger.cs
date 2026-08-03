using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using PcSetupMaintainer.Models;

namespace PcSetupMaintainer.Services;

public sealed class AppLogger
{
    private readonly string _logPath;
    private readonly object _fileLock = new();

    public ObservableCollection<OperationLogEntry> Entries { get; } = new();

    public AppLogger()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "PcSetupMaintainer",
            "Logs");
        Directory.CreateDirectory(dir);
        _logPath = Path.Combine(dir, $"session-{DateTimeOffset.Now:yyyyMMdd-HHmmss}.log");
    }

    public void Info(string message) => Write("INFO", message);

    public void Warn(string message) => Write("WARN", message);

    public void Error(string message) => Write("ERROR", message);

    public void Write(string level, string message)
    {
        var entry = new OperationLogEntry(DateTimeOffset.Now, level, message);
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
        {
            dispatcher.BeginInvoke(() => Entries.Add(entry));
        }
        else
        {
            Entries.Add(entry);
        }

        lock (_fileLock)
        {
            File.AppendAllText(_logPath, $"[{entry.Time:O}] [{level}] {message}{Environment.NewLine}");
        }
    }

    public string LogPath => _logPath;
}
