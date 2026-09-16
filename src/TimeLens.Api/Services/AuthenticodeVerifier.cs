using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace TimeLens.Api.Services;

internal static class AuthenticodeVerifier
{
    private static readonly Guid GenericVerifyV2 = new("00AAC56B-CD44-11D0-8CC2-00C04FC295EE");

    public static void EnsureTrustedUpdate(string installedExecutable, string candidateExecutable)
    {
        using var installedCertificate = TryReadCertificate(installedExecutable);
        using var candidateCertificate = TryReadCertificate(candidateExecutable);

        // Existing unsigned installations continue to update through the pinned
        // HTTPS feed plus SHA-256. Once a signed build is installed, signature and
        // publisher matching become mandatory for every subsequent update.
        if (installedCertificate is null)
        {
            if (candidateCertificate is not null && !HasValidSignature(candidateExecutable))
                throw new InvalidDataException("The update has an invalid Authenticode signature.");
            return;
        }

        if (candidateCertificate is null || !HasValidSignature(candidateExecutable))
            throw new InvalidDataException("The update is not signed by the installed publisher.");
        if (!string.Equals(installedCertificate.Subject, candidateCertificate.Subject, StringComparison.Ordinal) ||
            !CryptographicOperations.FixedTimeEquals(installedCertificate.GetPublicKey(), candidateCertificate.GetPublicKey()))
            throw new InvalidDataException("The update publisher does not match the installed application.");
    }

    private static X509Certificate2? TryReadCertificate(string path)
    {
        try
        {
#pragma warning disable SYSLIB0057 // The framework has no replacement API for extracting an Authenticode signer from a PE file.
            using var certificate = X509Certificate.CreateFromSignedFile(path);
#pragma warning restore SYSLIB0057
            return X509CertificateLoader.LoadCertificate(certificate.GetRawCertData());
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    private static bool HasValidSignature(string path)
    {
        var filePath = Marshal.StringToCoTaskMemUni(path);
        var fileInfoPointer = IntPtr.Zero;
        var trustDataPointer = IntPtr.Zero;
        try
        {
            var fileInfo = new WinTrustFileInfo
            {
                Size = (uint)Marshal.SizeOf<WinTrustFileInfo>(),
                FilePath = filePath
            };
            fileInfoPointer = Marshal.AllocCoTaskMem(Marshal.SizeOf<WinTrustFileInfo>());
            Marshal.StructureToPtr(fileInfo, fileInfoPointer, false);
            var trustData = new WinTrustData
            {
                Size = (uint)Marshal.SizeOf<WinTrustData>(),
                UiChoice = 2, // WTD_UI_NONE
                RevocationChecks = 0,
                UnionChoice = 1, // WTD_CHOICE_FILE
                File = fileInfoPointer,
                StateAction = 0,
                ProviderFlags = 0x1000 // WTD_CACHE_ONLY_URL_RETRIEVAL
            };
            trustDataPointer = Marshal.AllocCoTaskMem(Marshal.SizeOf<WinTrustData>());
            Marshal.StructureToPtr(trustData, trustDataPointer, false);
            var action = GenericVerifyV2;
            return WinVerifyTrust(IntPtr.Zero, ref action, trustDataPointer) == 0;
        }
        finally
        {
            if (trustDataPointer != IntPtr.Zero) Marshal.FreeCoTaskMem(trustDataPointer);
            if (fileInfoPointer != IntPtr.Zero) Marshal.FreeCoTaskMem(fileInfoPointer);
            Marshal.FreeCoTaskMem(filePath);
        }
    }

    [DllImport("wintrust.dll", ExactSpelling = true, PreserveSig = true)]
    private static extern int WinVerifyTrust(IntPtr window, ref Guid action, IntPtr trustData);

    [StructLayout(LayoutKind.Sequential)]
    private struct WinTrustFileInfo
    {
        public uint Size;
        public IntPtr FilePath;
        public IntPtr FileHandle;
        public IntPtr KnownSubject;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WinTrustData
    {
        public uint Size;
        public IntPtr PolicyCallbackData;
        public IntPtr SipClientData;
        public uint UiChoice;
        public uint RevocationChecks;
        public uint UnionChoice;
        public IntPtr File;
        public uint StateAction;
        public IntPtr StateData;
        public IntPtr UrlReference;
        public uint ProviderFlags;
        public uint UiContext;
    }
}
