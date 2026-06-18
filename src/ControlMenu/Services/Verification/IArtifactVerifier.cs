using ControlMenu.Modules;

namespace ControlMenu.Services.Verification;

public interface IArtifactVerifier
{
    Task<VerificationResult> VerifyAsync(
        string filePath, ModuleDependency dep, string version, CancellationToken ct);
}
