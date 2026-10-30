using M365Aegis.Core.Diagnostics;

namespace M365Aegis.Core.Tests.Diagnostics;

/// <summary>
/// The signature row of the doctor (task 085): which files are the product's own, the same names the release script
/// signs, and how a <c>WinVerifyTrust</c> answer reads. The Windows call itself runs in the <c>windows</c> job and on
/// a release folder; here its answers are given.
/// </summary>
public sealed class AuthenticodeTests
{
    [Fact]
    public void TheProductFilesAreTheExesAndAssembliesTheReleaseSigns()
    {
        string folder = Directory.CreateTempSubdirectory("m365-aegis-tests-").FullName;
        foreach (string name in new[]
        {
            "m365-aegis.exe", "m365-aegis-gui.exe", "m365-aegis.dll", "m365-aegis-gui.dll", "M365Aegis.Core.dll",
            "Microsoft.Identity.Client.dll", "msalruntime.dll", "m365-aegis.deps.json", "M365Aegis.Core.pdb", "THIRD-PARTY-NOTICES.md",
        })
        {
            File.WriteAllText(Path.Combine(folder, name), "");
        }

        Directory.CreateDirectory(Path.Combine(folder, "sidecar"));
        File.WriteAllText(Path.Combine(folder, "sidecar", "m365-aegis-other.dll"), "");

        Assert.Equal(
            ["m365-aegis-gui.dll", "m365-aegis-gui.exe", "m365-aegis.dll", "m365-aegis.exe", "M365Aegis.Core.dll"],
            Authenticode.ProductFiles(folder));
    }

    [Fact]
    public void AMissingFolderHasNoProductFiles() =>
        Assert.Empty(Authenticode.ProductFiles(Path.Combine(Path.GetTempPath(), "m365-aegis-no-such-folder")));

    [Theory]
    [InlineData(0, SignatureState.Valid)]
    [InlineData(Authenticode.NoSignature, SignatureState.NotSigned)]
    [InlineData(Authenticode.SubjectFormUnknown, SignatureState.NotSigned)]
    [InlineData(Authenticode.TrustProviderUnknown, SignatureState.NotSigned)]
    [InlineData(Authenticode.UntrustedRoot, SignatureState.Untrusted)]
    [InlineData(Authenticode.Chaining, SignatureState.Untrusted)]
    [InlineData(Authenticode.UntrustedTestRoot, SignatureState.Untrusted)]
    [InlineData(Authenticode.ExplicitDistrust, SignatureState.Untrusted)]
    [InlineData(Authenticode.SubjectNotTrusted, SignatureState.Untrusted)]
    [InlineData(Authenticode.Expired, SignatureState.Untrusted)]
    [InlineData(Authenticode.BadDigest, SignatureState.Broken)]
    [InlineData(unchecked((int)0x800B010C), SignatureState.Broken)]
    public void AWinVerifyTrustAnswerReadsAsAState(int code, SignatureState state) =>
        Assert.Equal(state, Authenticode.Classify(code));
}
