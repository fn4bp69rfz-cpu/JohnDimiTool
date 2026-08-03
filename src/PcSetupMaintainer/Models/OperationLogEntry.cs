namespace PcSetupMaintainer.Models;

public sealed record OperationLogEntry(DateTimeOffset Time, string Level, string Message);
