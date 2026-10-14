using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using FanForge.Core;

namespace FanForge;

/// <summary>
/// Validates actual Windows Authenticode chain/file integrity via WinVerifyTrust.
/// Certificate extraction alone does NOT verify a signature. Verification is offline
/// (no remote revocation retrieval) and must not be represented as online revocation proof.
/// </summary>
internal static class OemLibraryVerifier
{
    private static readonly Guid WintrustActionGenericVerifyV2 =
        new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WintrustFileInfo
    {
        public uint cbStruct;
        [MarshalAs(UnmanagedType.LPWStr)] public string pcwszFilePath;
        public IntPtr hFile;
        public IntPtr pgKnownSubject;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WintrustData
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

    [DllImport("wintrust.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    private static extern int WinVerifyTrust(IntPtr hwnd,
        [MarshalAs(UnmanagedType.LPStruct)] Guid actionId, IntPtr trustData);

    public static (Version? Version, string? Signer, bool Trusted) Inspect(string dllPath)
    {
        var fileVersion = FileVersionInfo.GetVersionInfo(dllPath).FileVersion;
        Version? version = Version.TryParse(fileVersion, out var parsed) ? parsed : null;
        string? signer = null;
        try
        {
            using X509Certificate certificate = X509Certificate.CreateFromSignedFile(dllPath);
            signer = certificate.Subject;
        }
        catch (CryptographicException) { }
        catch (ArgumentException) { }

        // A signer string without WinVerifyTrust succeeds at nothing.
        bool valid = false;
        if (OemLibraryPolicy.VersionSupported(version) && OemLibraryPolicy.TrustedSigner(signer))
        {
            try { valid = VerifyAuthenticode(dllPath); }
            catch (Exception ex) when (ex is ExternalException or DllNotFoundException
                or EntryPointNotFoundException or ArgumentException)
            {
                valid = false;
            }
        }
        return (version, signer, OemLibraryPolicy.MayLoad(version, signer, valid));
    }

    private static bool VerifyAuthenticode(string path)
    {
        var file = new WintrustFileInfo
        {
            cbStruct = (uint)Marshal.SizeOf<WintrustFileInfo>(),
            pcwszFilePath = path,
            hFile = IntPtr.Zero,
            pgKnownSubject = IntPtr.Zero
        };
        IntPtr filePtr = IntPtr.Zero, dataPtr = IntPtr.Zero;
        bool fileMarshaled = false;
        try
        {
            filePtr = Marshal.AllocHGlobal(Marshal.SizeOf<WintrustFileInfo>());
            Marshal.StructureToPtr(file, filePtr, false);
            fileMarshaled = true;
            var data = new WintrustData
            {
                cbStruct = (uint)Marshal.SizeOf<WintrustData>(),
                dwUIChoice = 2,          // WTD_UI_NONE
                fdwRevocationChecks = 0, // offline: WTD_REVOKE_NONE
                dwUnionChoice = 1,       // WTD_CHOICE_FILE
                pFile = filePtr,
                dwStateAction = 0,       // WTD_STATEACTION_IGNORE
                dwProvFlags = 0x1010    // REVOCATION_CHECK_NONE | CACHE_ONLY_URL_RETRIEVAL
            };
            dataPtr = Marshal.AllocHGlobal(Marshal.SizeOf<WintrustData>());
            Marshal.StructureToPtr(data, dataPtr, false);
            return WinVerifyTrust(IntPtr.Zero, WintrustActionGenericVerifyV2, dataPtr) == 0;
        }
        finally
        {
            if (dataPtr != IntPtr.Zero) Marshal.FreeHGlobal(dataPtr);
            if (filePtr != IntPtr.Zero)
            {
                if (fileMarshaled) Marshal.DestroyStructure<WintrustFileInfo>(filePtr);
                Marshal.FreeHGlobal(filePtr);
            }
        }
    }
}
