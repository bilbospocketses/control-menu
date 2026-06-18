namespace ControlMenu.Services.Verification;

public enum VerificationTier { PinnedHash, UpstreamChecksum, Authenticode, Unverified }

public record VerificationResult(bool Verified, VerificationTier Tier, string? Algorithm, string Detail);
