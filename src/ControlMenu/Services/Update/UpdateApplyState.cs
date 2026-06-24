namespace ControlMenu.Services.Update;

/// <summary>
/// Shared flag set by <see cref="VelopackUpdateService.RequestApplyUpdate"/> and read by
/// <c>Program</c> after the host stops, so the process returns the apply-update exit code
/// explicitly (a clean <c>return</c> from Main) instead of via a clobberable
/// <see cref="System.Environment.ExitCode"/>. Registered as a singleton.
/// </summary>
public sealed class UpdateApplyState
{
    private volatile bool _applyRequested;

    public bool ApplyRequested
    {
        get => _applyRequested;
        set => _applyRequested = value;
    }
}
