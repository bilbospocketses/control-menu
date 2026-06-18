using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography.X509Certificates;

namespace ControlMenu.Services.Verification;

/// <summary>
/// Reads Authenticode signer + trust via WinVerifyTrust. On non-Windows returns IsSigned=false.
/// </summary>
public sealed class WindowsAuthenticodeInspector : IAuthenticodeInspector
{
    public AuthenticodeInfo Inspect(string filePath)
    {
        if (!OperatingSystem.IsWindows()) return new AuthenticodeInfo(false, false, null);

        string? subjectCn = null;
        var signed = false;
        try
        {
            // SYSLIB0057: no X509CertificateLoader equivalent for embedded Authenticode PE signatures;
            // CreateFromSignedFile is the only BCL API that reads the embedded cert from a PE.
#pragma warning disable SYSLIB0057
            using var cert = new X509Certificate2(X509Certificate.CreateFromSignedFile(filePath));
#pragma warning restore SYSLIB0057
            signed = true;
            // Use structural parse via GetNameInfo — handles RFC-2253 escaped commas in Subject.
            // Reconstruct "CN=<leafCN>" to match the ExpectedSigner pin format ("CN=Google LLC").
            var leafCn = cert.GetNameInfo(X509NameType.SimpleName, forIssuer: false);
            subjectCn = string.IsNullOrEmpty(leafCn) ? null : "CN=" + leafCn;
        }
        catch { return new AuthenticodeInfo(false, false, null); }

        // WinVerifyTrust opens the file independently by path; two reads are acceptable because the
        // target is a controlled just-downloaded temp artifact (path is stable between these calls).
        var trusted = WinVerifyTrustValid(filePath);
        return new AuthenticodeInfo(signed, trusted, subjectCn);
    }

    // Revocation policy (user decision 2026-06-17):
    //   - Revocation IS checked (WTD_REVOKE_WHOLECHAIN + WTD_REVOCATION_CHECK_CHAIN).
    //   - A definitively revoked cert (CERT_E_REVOKED) -> untrusted; hard fail.
    //   - If revocation status cannot be determined (offline/unreachable):
    //     CERT_E_REVOCATION_FAILURE or CRYPT_E_REVOCATION_OFFLINE -> treated as trusted-modulo-revocation
    //     so legitimate offline updates still work.
    //   - Any other non-zero HRESULT (e.g. tampered digest) -> untrusted.
    private const int S_OK = 0;
    private const int CERT_E_REVOCATION_FAILURE  = unchecked((int)0x800B010E); // revocation status unknown
    private const int CRYPT_E_REVOCATION_OFFLINE = unchecked((int)0x80092013); // responder offline
    private const int CERT_E_REVOKED             = unchecked((int)0x800B010C); // definitively revoked

    /// <summary>
    /// Maps a WinVerifyTrust HRESULT to the trusted/untrusted decision using the offline-tolerant policy.
    /// Exposed as internal for unit testing without requiring a cert or network.
    /// </summary>
    internal static bool IsTrustedResult(int hr) =>
        hr == S_OK || hr == CERT_E_REVOCATION_FAILURE || hr == CRYPT_E_REVOCATION_OFFLINE;

    [SupportedOSPlatform("windows")]
    private static bool WinVerifyTrustValid(string filePath)
    {
        var actionId = new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE"); // WINTRUST_ACTION_GENERIC_VERIFY_V2
        var fileInfo = new WINTRUST_FILE_INFO
        {
            cbStruct = (uint)Marshal.SizeOf<WINTRUST_FILE_INFO>(),
            pcwszFilePath = filePath
        };
        var pFile = Marshal.AllocHGlobal(Marshal.SizeOf<WINTRUST_FILE_INFO>());
        try
        {
            Marshal.StructureToPtr(fileInfo, pFile, false);
            var data = new WINTRUST_DATA
            {
                cbStruct = (uint)Marshal.SizeOf<WINTRUST_DATA>(),
                dwUIChoice = 2,                // WTD_UI_NONE
                fdwRevocationChecks = 1,       // WTD_REVOKE_WHOLECHAIN
                dwUnionChoice = 1,             // WTD_CHOICE_FILE
                pFile = pFile,
                dwStateAction = 0,
                dwProvFlags = 0x10 | 0x40      // WTD_SAFER_FLAG | WTD_REVOCATION_CHECK_CHAIN
            };
            int hr = WinVerifyTrust(IntPtr.Zero, ref actionId, ref data);
            return IsTrustedResult(hr);
        }
        finally { Marshal.FreeHGlobal(pFile); }
    }

    [DllImport("wintrust.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int WinVerifyTrust(IntPtr hwnd, ref Guid actionId, ref WINTRUST_DATA data);

    [StructLayout(LayoutKind.Sequential)]
    private struct WINTRUST_FILE_INFO
    {
        public uint cbStruct;
        [MarshalAs(UnmanagedType.LPWStr)] public string pcwszFilePath;
        public IntPtr hFile;
        public IntPtr pgKnownSubject;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WINTRUST_DATA
    {
        public uint cbStruct;
        public IntPtr pPolicyCallbackData;
        public IntPtr pSIPClientData;
        public uint dwUIChoice;
        public uint fdwRevocationChecks;
        public uint dwUnionChoice;
        public IntPtr pFile;
        public uint dwStateAction;
        public IntPtr hWVTStateData;
        public IntPtr pwszURLReference;
        public uint dwProvFlags;
        public uint dwUIContext;
        public IntPtr pSignatureSettings;
    }
}
