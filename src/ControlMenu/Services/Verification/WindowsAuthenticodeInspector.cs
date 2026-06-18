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
            subjectCn = cert.Subject; // full subject; compared with StartsWith/Equals on "CN=..."
        }
        catch { return new AuthenticodeInfo(false, false, null); }

        var trusted = WinVerifyTrustValid(filePath);
        // Normalise: callers compare against "CN=Google LLC", so surface the CN= component.
        var cn = ExtractCn(subjectCn);
        return new AuthenticodeInfo(signed, trusted, cn);
    }

    private static string? ExtractCn(string? subject)
    {
        if (subject is null) return null;
        foreach (var part in subject.Split(','))
        {
            var p = part.Trim();
            if (p.StartsWith("CN=", StringComparison.OrdinalIgnoreCase)) return p;
        }
        return subject;
    }

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
                dwUIChoice = 2,            // WTD_UI_NONE
                fdwRevocationChecks = 0,   // WTD_REVOKE_NONE
                dwUnionChoice = 1,         // WTD_CHOICE_FILE
                pFile = pFile,
                dwStateAction = 0,
                dwProvFlags = 0x10         // WTD_SAFER_FLAG
            };
            int hr = WinVerifyTrust(IntPtr.Zero, ref actionId, ref data);
            return hr == 0; // 0 == trusted
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
