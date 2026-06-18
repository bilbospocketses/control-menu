namespace ControlMenu.Services.Verification;

public record AuthenticodeInfo(bool IsSigned, bool IsTrusted, string? SubjectCn);

public interface IAuthenticodeInspector
{
    /// <summary>Read Authenticode state for a file. Non-Windows / unsigned -> IsSigned=false.</summary>
    AuthenticodeInfo Inspect(string filePath);
}
