namespace ControlMenu.Services.Network;

public abstract record ScanEvent;

public sealed record ScanStartedEvent(int TotalHosts, int TotalSubnets, long StartedAt) : ScanEvent;
public sealed record ScanProgressEvent(int Checked, int Total, int FoundSoFar) : ScanEvent;
public sealed record ScanHitEvent(ScanHit Hit) : ScanEvent;
public sealed record ScanDrainingEvent : ScanEvent;
public sealed record ScanCompleteEvent(int Found) : ScanEvent;
public sealed record ScanCancelledEvent(int Found) : ScanEvent;
public sealed record ScanErrorEvent(string Reason) : ScanEvent;
