namespace ControlMenu.Modules.Cameras.Network;

public abstract record CameraScanEvent;

public sealed record CameraScanStartedEvent(IReadOnlyList<string> Subnets) : CameraScanEvent;
public sealed record CameraScanHitEvent(CameraScanHit Hit) : CameraScanEvent;
public sealed record CameraScanProgressEvent(int Probed, int Total) : CameraScanEvent;
public sealed record CameraScanCompletedEvent(int OnvifHits, int RtspHits, TimeSpan Duration) : CameraScanEvent;
public sealed record CameraScanCancelledEvent : CameraScanEvent;
public sealed record CameraScanErrorEvent(string Branch, string Message) : CameraScanEvent;
