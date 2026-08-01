using D2BotNG.Services.Analytics;
using Xunit;

namespace D2BotNG.Tests.Services;

/// <summary>
/// The install id is derived independently by two codebases — this one and d2bsng's
/// DeriveInstallId (src/frontends/js/components/analytics/Analytics.cpp) — so that a machine's
/// manager and its injected DLLs report the same install. Nothing links the two implementations
/// at build time, so a drifted salt, field order, separator or hex casing would not fail to
/// compile: it would silently split every install into two, and only show up as a broken
/// dashboard long after release.
///
/// These pin the format. If one of them fails, the corresponding change has to be made in
/// d2bsng too (and deliberately, since it re-buckets every existing install as new).
/// </summary>
public class InstallIdTests
{
    // salt|machineGuid|volumeSerial|computerName, UTF-8, SHA-256, lowercase hex.
    private const string KnownDigest = "6645c130e2c094fe0c93cbca6fb2501ac0ce5c6e314be06506e7e8fcfe18f1b2";

    [Fact]
    public void MatchesTheSharedDerivation()
    {
        var id = InstallId.Compute("11111111-2222-3333-4444-555555555555", "3735928559", "TESTPC");
        Assert.Equal(KnownDigest, id);
    }

    [Fact]
    public void IsLowercaseHexSha256()
    {
        var id = InstallId.Compute("guid", "1", "PC");

        Assert.Equal(64, id.Length);
        Assert.DoesNotContain(id, char.IsUpper);
    }

    [Theory]
    // Each field must land in its own slot: swapping two inputs has to change the digest,
    // which it only does if they are separated rather than concatenated.
    [InlineData("a", "b", "c")]
    [InlineData("b", "a", "c")]
    [InlineData("a", "c", "b")]
    public void EveryFieldAffectsTheDigest(string machineGuid, string volumeSerial, string computerName)
    {
        var id = InstallId.Compute(machineGuid, volumeSerial, computerName);
        var baseline = InstallId.Compute("a", "b", "c");

        var isBaselineInput = machineGuid == "a" && volumeSerial == "b" && computerName == "c";
        Assert.Equal(isBaselineInput, id == baseline);
    }

    [Fact]
    public void MissingMachineFactsStillProduceAnId()
    {
        // Some Wine prefixes report no MachineGuid; the other two facts still separate installs.
        var id = InstallId.Compute("", "0", "PC");
        Assert.Equal(64, id.Length);
        Assert.NotEqual(InstallId.Compute("", "0", "OTHER"), id);
    }
}
