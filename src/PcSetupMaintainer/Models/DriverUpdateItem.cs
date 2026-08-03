using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace PcSetupMaintainer.Models;

public sealed class DriverUpdateItem : INotifyPropertyChanged
{
    private bool _isSelected;
    private string _status = "Ready";

    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Category { get; init; }
    public required string Provider { get; init; }
    public required string Source { get; init; }
    public required string Action { get; init; }
    public string Version { get; init; } = "";
    public string Notes { get; init; } = "";
    public bool IsFirmwareOrBios { get; init; }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) return;
            _isSelected = value;
            OnPropertyChanged();
        }
    }

    public string Status
    {
        get => _status;
        set
        {
            if (_status == value) return;
            _status = value;
            OnPropertyChanged();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
