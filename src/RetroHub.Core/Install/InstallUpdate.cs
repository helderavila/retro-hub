namespace RetroHub.Core.Install;

/// <summary>
/// Install progress event. Null fields mean "no change":
/// the UI keeps the previous stage/fraction and only appends the log line.
/// </summary>
public sealed record InstallUpdate(string? Stage = null, double? Fraction = null, string? Log = null)
{
    public static InstallUpdate Line(string log) => new(Log: log);
}
