using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Win32.SafeHandles;

namespace M1vClock.Core.Security;

public sealed record ExpectedPublisher(string CommonName, string Organization)
{
    public static ExpectedPublisher Microsoft { get; } = new("Microsoft Corporation", "Microsoft Corporation");

    public static ExpectedPublisher Corsair { get; } = new("Corsair Memory, Inc.", "Corsair Memory, Inc.");

    public static ExpectedPublisher Nvidia { get; } = new("NVIDIA Corporation", "NVIDIA Corporation");

    public static ExpectedPublisher Amd { get; } = new("Advanced Micro Devices, Inc.", "Advanced Micro Devices, Inc.");
}

public enum TrustStatus
{
    Trusted,
    NotSigned,
    InvalidSignature,
    UnexpectedPublisher,
}

public sealed record TrustResult(TrustStatus Status, int ErrorCode, string? CommonName, string? Organization)
{
    public bool IsTrusted => Status == TrustStatus.Trusted;
}

public interface ITrustVerifier
{
    TrustResult Verify(string filePath, ExpectedPublisher expected);

    TrustResult Verify(SafeFileHandle openFile, string filePath, ExpectedPublisher expected);
}

public sealed partial class TrustVerifier : ITrustVerifier
{
    private const uint UiNone = 2;
    private const uint RevokeWholeChain = 1;
    private const uint ChoiceFile = 1;
    private const uint StateActionVerify = 1;
    private const uint StateActionClose = 2;
    private const uint RevocationCheckChainExcludeRoot = 0x00000080;
    private const int TrustNoSignature = unchecked((int)0x800B0100);
    private const int TrustSubjectFormUnknown = unchecked((int)0x800B0003);
    private const int TrustProviderUnknown = unchecked((int)0x800B0001);
    private const string CommonNameOid = "2.5.4.3";
    private const string OrganizationOid = "2.5.4.10";

    private static readonly Guid GenericVerifyV2 = new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");

    public TrustResult Verify(string filePath, ExpectedPublisher expected)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        using var handle = File.OpenHandle(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Verify(handle, filePath, expected);
    }

    public TrustResult Verify(SafeFileHandle openFile, string filePath, ExpectedPublisher expected)
    {
        ArgumentNullException.ThrowIfNull(openFile);
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentNullException.ThrowIfNull(expected);

        var path = Marshal.StringToHGlobalUni(Path.GetFullPath(filePath));
        var fileInfo = Marshal.AllocHGlobal(Marshal.SizeOf<WinTrustFileInfo>());
        var addRef = false;
        try
        {
            openFile.DangerousAddRef(ref addRef);
            Marshal.StructureToPtr(
                new WinTrustFileInfo
                {
                    CbStruct = (uint)Marshal.SizeOf<WinTrustFileInfo>(),
                    FilePath = path,
                    File = openFile.DangerousGetHandle(),
                    KnownSubject = IntPtr.Zero,
                },
                fileInfo,
                false);

            var data = new WinTrustData
            {
                CbStruct = (uint)Marshal.SizeOf<WinTrustData>(),
                UiChoice = UiNone,
                RevocationChecks = RevokeWholeChain,
                UnionChoice = ChoiceFile,
                File = fileInfo,
                StateAction = StateActionVerify,
                ProvFlags = RevocationCheckChainExcludeRoot,
            };

            var action = GenericVerifyV2;
            var code = WinVerifyTrust(new IntPtr(-1), ref action, ref data);
            try
            {
                if (code is TrustNoSignature or TrustSubjectFormUnknown or TrustProviderUnknown)
                {
                    return new TrustResult(TrustStatus.NotSigned, code, null, null);
                }

                if (code != 0)
                {
                    return new TrustResult(TrustStatus.InvalidSignature, code, null, null);
                }

                var (commonName, organization) = ReadSigner(data.StateData);
                var matches = string.Equals(commonName, expected.CommonName, StringComparison.Ordinal)
                    && string.Equals(organization, expected.Organization, StringComparison.Ordinal);
                return new TrustResult(matches ? TrustStatus.Trusted : TrustStatus.UnexpectedPublisher, 0, commonName, organization);
            }
            finally
            {
                data.StateAction = StateActionClose;
                _ = WinVerifyTrust(new IntPtr(-1), ref action, ref data);
            }
        }
        finally
        {
            if (addRef)
            {
                openFile.DangerousRelease();
            }

            Marshal.DestroyStructure<WinTrustFileInfo>(fileInfo);
            Marshal.FreeHGlobal(fileInfo);
            Marshal.FreeHGlobal(path);
        }
    }

    private static (string? CommonName, string? Organization) ReadSigner(IntPtr stateData)
    {
        var providerData = WTHelperProvDataFromStateData(stateData);
        if (providerData == IntPtr.Zero)
        {
            return (null, null);
        }

        var signer = WTHelperGetProvSignerFromChain(providerData, 0, false, 0);
        if (signer == IntPtr.Zero)
        {
            return (null, null);
        }

        var signerInfo = Marshal.PtrToStructure<CryptProviderSigner>(signer);
        if (signerInfo.CertChainCount == 0 || signerInfo.CertChain == IntPtr.Zero)
        {
            return (null, null);
        }

        var leaf = Marshal.PtrToStructure<CryptProviderCert>(signerInfo.CertChain);
        using var certificate = new X509Certificate2(leaf.Certificate);
        string? commonName = null;
        string? organization = null;
        foreach (var rdn in certificate.SubjectName.EnumerateRelativeDistinguishedNames())
        {
            var oid = rdn.GetSingleElementType().Value;
            if (oid == CommonNameOid)
            {
                commonName ??= rdn.GetSingleElementValue();
            }
            else if (oid == OrganizationOid)
            {
                organization ??= rdn.GetSingleElementValue();
            }
        }

        return (commonName, organization);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WinTrustFileInfo
    {
        public uint CbStruct;
        public IntPtr FilePath;
        public IntPtr File;
        public IntPtr KnownSubject;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WinTrustData
    {
        public uint CbStruct;
        public IntPtr PolicyCallbackData;
        public IntPtr SipClientData;
        public uint UiChoice;
        public uint RevocationChecks;
        public uint UnionChoice;
        public IntPtr File;
        public uint StateAction;
        public IntPtr StateData;
        public IntPtr UrlReference;
        public uint ProvFlags;
        public uint UiContext;
        public IntPtr SignatureSettings;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CryptProviderSigner
    {
        public uint CbStruct;
        public uint VerifyAsOfLow;
        public uint VerifyAsOfHigh;
        public uint CertChainCount;
        public IntPtr CertChain;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CryptProviderCert
    {
        public uint CbStruct;
        public IntPtr Certificate;
    }

    [LibraryImport("wintrust.dll")]
    private static partial int WinVerifyTrust(IntPtr window, ref Guid action, ref WinTrustData data);

    [LibraryImport("wintrust.dll")]
    private static partial IntPtr WTHelperProvDataFromStateData(IntPtr stateData);

    [LibraryImport("wintrust.dll")]
    private static partial IntPtr WTHelperGetProvSignerFromChain(
        IntPtr providerData,
        uint signerIndex,
        [MarshalAs(UnmanagedType.Bool)] bool counterSigner,
        uint counterSignerIndex);
}
