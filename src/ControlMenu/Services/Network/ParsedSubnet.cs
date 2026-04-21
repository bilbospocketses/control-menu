namespace ControlMenu.Services.Network;

/// <summary>
/// Normalized form of a user-entered subnet. <see cref="Raw"/> is what the user typed;
/// <see cref="Normalized"/> is the canonical form (e.g. <c>192.168.1.0/24</c> or
/// <c>192.168.1.10-192.168.1.50</c>). <see cref="HostCount"/> is the effective number
/// of scannable hosts (network/broadcast excluded for CIDR).
/// </summary>
public sealed record ParsedSubnet(string Raw, string Normalized, int HostCount);
