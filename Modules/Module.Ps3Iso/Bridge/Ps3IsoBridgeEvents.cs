namespace Module.Ps3Iso.Bridge;

public abstract record Ps3IsoEvent;

public sealed record Ps3IsoStatusEvent(string ZipName, string Phase, string Message, string? IsoFilename = null) : Ps3IsoEvent;
