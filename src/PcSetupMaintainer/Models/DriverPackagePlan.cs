namespace PcSetupMaintainer.Models;

public sealed record HardwareSummary(
    string Manufacturer,
    string Model,
    string BaseBoard,
    string BiosVersion,
    string WindowsVersion,
    IReadOnlyList<string> DisplayAdapters,
    IReadOnlyList<string> NetworkAdapters,
    IReadOnlyList<string> AudioDevices);

public sealed record DriverSource(
    string Category,
    string Name,
    string Provider,
    string Strategy,
    string CommandOrUrl,
    bool SupportsOfflinePackage,
    string Notes);

public sealed record DriverPackagePlan(
    HardwareSummary Hardware,
    IReadOnlyList<DriverSource> Sources,
    string PackageRoot,
    string SetupScriptPath);
