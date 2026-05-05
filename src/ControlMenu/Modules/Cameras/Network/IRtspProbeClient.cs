namespace ControlMenu.Modules.Cameras.Network;

public interface IRtspProbeClient
{
    Task<bool> ProbeTcpAsync(string ip, int port, TimeSpan timeout, CancellationToken ct);
    Task<RtspDescribeResult> DescribeAsync(string rtspUrl, TimeSpan timeout, CancellationToken ct);
}

public sealed record RtspDescribeResult(
    bool Success,
    int StatusCode,        // 200, 401, 404, 0 for connection failure
    string? StatusReason,
    string? Sdp);          // SDP body when 200
