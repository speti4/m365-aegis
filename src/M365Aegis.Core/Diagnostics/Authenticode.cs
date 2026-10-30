using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography.X509Certificates;
using System.Text.RegularExpressions;

namespace M365Aegis.Core.Diagnostics;

/// <summary>What Windows says about a file's Authenticode signature (task 085).</summary>
public enum SignatureState
{
    /// <summary>Signed, the file unchanged since, by a certificate this machine trusts.</summary>
    Valid,

    /// <summary>No signature: a build from source, or an unsigned release zip.</summary>
    NotSigned,

    /// <summary>Signed and unchanged, but the certificate is not trusted here (an untrusted or distrusted root, or expired without a timestamp).</summary>
    Untrusted,

    /// <summary>The signature does not hold: the file was changed after it was signed, or the signature is damaged or revoked.</summary>
    Broken,
}

/// <summary>One file's Authenticode signature as the doctor reads it.</summary>
/// <param name="File">The file name, without the folder.</param>
/// <param name="State">The verdict.</param>
/// <param name="Signer">The signing certificate's subject, or null when there is none to read.</param>
/// <param name="Code">The <c>WinVerifyTrust</c> answer (an HRESULT, 0 for a valid signature).</param>
public sealed record FileSignature(string File, SignatureState State, string? Signer, int Code);

/// <summary>
/// The Authenticode signatures of the product's own files (task 085): the files the release script signs, found by
/// the same names (<c>m365-aegis*.exe</c>, <c>m365-aegis*.dll</c>, <c>M365Aegis.*.dll</c>), and the verdict of
/// <c>WinVerifyTrust</c> over each one, without revocation checks and without a network fetch, so the offline doctor
/// stays offline.
/// </summary>
public static partial class Authenticode
{
    /// <summary>The <c>WinVerifyTrust</c> answers the doctor tells apart; any other is <see cref="SignatureState.Broken"/>.</summary>
    internal const int TrustProviderUnknown = unchecked((int)0x800B0001);
    internal const int SubjectFormUnknown = unchecked((int)0x800B0003);
    internal const int SubjectNotTrusted = unchecked((int)0x800B0004);
    internal const int NoSignature = unchecked((int)0x800B0100);
    internal const int Expired = unchecked((int)0x800B0101);
    internal const int UntrustedRoot = unchecked((int)0x800B0109);
    internal const int Chaining = unchecked((int)0x800B010A);
    internal const int UntrustedTestRoot = unchecked((int)0x800B010D);
    internal const int ExplicitDistrust = unchecked((int)0x800B0111);
    internal const int BadDigest = unchecked((int)0x80096010);

    /// <summary>Why the Windows calls below are excluded from the coverage (docs/development.md, Coverage).</summary>
    private const string ThisMachine = "Win32 calls over the files of this machine, which Linux runners do not have (docs/development.md, Coverage)";

    /// <summary>The names of the files the release signs, in a folder: the product's own exes and assemblies.</summary>
    internal static IReadOnlyList<string> ProductFiles(string folder) => Directory.Exists(folder)
        ? [.. Directory.EnumerateFiles(folder).Select(Path.GetFileName).OfType<string>().Where(name => ProductFile().IsMatch(name)).Order(StringComparer.OrdinalIgnoreCase)]
        : [];

    /// <summary>The verdict of a <c>WinVerifyTrust</c> answer.</summary>
    internal static SignatureState Classify(int code) => code switch
    {
        0 => SignatureState.Valid,
        NoSignature or SubjectFormUnknown or TrustProviderUnknown => SignatureState.NotSigned,
        UntrustedRoot or Chaining or UntrustedTestRoot or ExplicitDistrust or SubjectNotTrusted or Expired => SignatureState.Untrusted,
        _ => SignatureState.Broken,
    };

    /// <summary>The product's files next to the running assembly, each with its signature; empty off Windows.</summary>
    [ExcludeFromCodeCoverage(Justification = ThisMachine)]
    internal static IReadOnlyList<FileSignature> ReadProductFiles()
    {
        if (!OperatingSystem.IsWindows())
        {
            return [];
        }

        string folder = AppContext.BaseDirectory;
        var signatures = new List<FileSignature>();
        foreach (string name in ProductFiles(folder))
        {
            (int code, string? signer) = Verify(Path.Combine(folder, name));
            signatures.Add(new FileSignature(name, Classify(code), signer, code));
        }

        return signatures;
    }

    /// <summary>
    /// <c>WinVerifyTrust</c> with the generic Authenticode policy over one file: no user interface, no revocation
    /// check, only what the local caches hold (no download); the signer's subject from the state it leaves.
    /// </summary>
    [SupportedOSPlatform("windows")]
    [ExcludeFromCodeCoverage(Justification = ThisMachine)]
    private static (int Code, string? Signer) Verify(string path)
    {
        IntPtr pathText = Marshal.StringToHGlobalUni(path);
        IntPtr fileInfo = Marshal.AllocHGlobal(Marshal.SizeOf<FileInfo>());
        try
        {
            Marshal.StructureToPtr(new FileInfo { Size = (uint)Marshal.SizeOf<FileInfo>(), FilePath = pathText }, fileInfo, fDeleteOld: false);
            var data = new TrustData
            {
                Size = (uint)Marshal.SizeOf<TrustData>(),
                UiChoice = 2, // WTD_UI_NONE
                RevocationChecks = 0, // WTD_REVOKE_NONE
                UnionChoice = 1, // WTD_CHOICE_FILE
                File = fileInfo,
                StateAction = 1, // WTD_STATEACTION_VERIFY: keep the state, to read the signer
                ProviderFlags = 0x1000 | 0x10, // WTD_CACHE_ONLY_URL_RETRIEVAL | WTD_REVOCATION_CHECK_NONE
            };
            Guid action = GenericVerifyV2;
            int code = WinVerifyTrust(IntPtr.Zero, ref action, ref data);
            string? signer = Signer(data.StateData);
            data.StateAction = 2; // WTD_STATEACTION_CLOSE
            _ = WinVerifyTrust(IntPtr.Zero, ref action, ref data);
            return (code, signer);
        }
        finally
        {
            Marshal.FreeHGlobal(fileInfo);
            Marshal.FreeHGlobal(pathText);
        }
    }

    /// <summary>The subject of the first signer's certificate in the state <c>WinVerifyTrust</c> left, or null.</summary>
    [SupportedOSPlatform("windows")]
    [ExcludeFromCodeCoverage(Justification = ThisMachine)]
    private static string? Signer(IntPtr state)
    {
        if (state == IntPtr.Zero)
        {
            return null;
        }

        IntPtr provider = WTHelperProvDataFromStateData(state);
        IntPtr signer = provider == IntPtr.Zero ? IntPtr.Zero : WTHelperGetProvSignerFromChain(provider, 0, fCounterSigner: 0, 0);
        if (signer == IntPtr.Zero)
        {
            return null;
        }

        ProviderSigner read = Marshal.PtrToStructure<ProviderSigner>(signer);
        if (read.CertificateCount == 0 || read.Certificates == IntPtr.Zero)
        {
            return null;
        }

        IntPtr context = Marshal.PtrToStructure<ProviderCertificate>(read.Certificates).Certificate;
        if (context == IntPtr.Zero)
        {
            return null;
        }

        // A copy of the context: the state owns the original, and closing it frees it.
        using var certificate = new X509Certificate2(context);
        return certificate.Subject;
    }

    /// <summary><c>WINTRUST_ACTION_GENERIC_VERIFY_V2</c>: the Authenticode policy.</summary>
    private static readonly Guid GenericVerifyV2 = new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

    [GeneratedRegex(@"^(?:m365-aegis[A-Za-z0-9-]*\.(?:exe|dll)|M365Aegis\.[A-Za-z0-9.]+\.dll)$", RegexOptions.IgnoreCase)]
    private static partial Regex ProductFile();

    [SupportedOSPlatform("windows")]
    [LibraryImport("wintrust.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int WinVerifyTrust(IntPtr window, ref Guid action, ref TrustData data);

    [SupportedOSPlatform("windows")]
    [LibraryImport("wintrust.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial IntPtr WTHelperProvDataFromStateData(IntPtr state);

    [SupportedOSPlatform("windows")]
    [LibraryImport("wintrust.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial IntPtr WTHelperGetProvSignerFromChain(IntPtr provider, uint signer, int fCounterSigner, uint counterSigner);

    /// <summary><c>WINTRUST_FILE_INFO</c>.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct FileInfo
    {
        public uint Size;
        public IntPtr FilePath;
        public IntPtr FileHandle;
        public IntPtr KnownSubject;
    }

    /// <summary><c>WINTRUST_DATA</c>, the file member of its union.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct TrustData
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
        public IntPtr SignatureSettings;
    }

    /// <summary>The head of <c>CRYPT_PROVIDER_SGNR</c>: its size, the time it was verified as of (a <c>FILETIME</c>), the chain.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct ProviderSigner
    {
        public uint Size;
        public uint VerifyAsOfLow;
        public uint VerifyAsOfHigh;
        public uint CertificateCount;
        public IntPtr Certificates;
    }

    /// <summary>The head of <c>CRYPT_PROVIDER_CERT</c>: its size and the certificate context.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct ProviderCertificate
    {
        public uint Size;
        public IntPtr Certificate;
    }
}
