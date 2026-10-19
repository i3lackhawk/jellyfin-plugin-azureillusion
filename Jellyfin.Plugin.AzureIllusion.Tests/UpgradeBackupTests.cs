using Jellyfin.Plugin.AzureIllusion.State;

namespace Jellyfin.Plugin.AzureIllusion.Tests;

public sealed class UpgradeBackupTests
{
    [Fact]
    public void SnapshotIncludesConfigurationRegistryBackupAndRecoveryJournal()
    {
        WithDirectory(root =>
        {
            var state = Path.Combine(root, "AzureIllusion");
            Directory.CreateDirectory(Path.Combine(state, "transactions"));
            File.WriteAllText(Path.Combine(root, "config.xml"), "private-config-test-only");
            File.WriteAllText(Path.Combine(state, "downloads.json"), "original-registry");
            File.WriteAllText(Path.Combine(state, "downloads.json.bak"), "previous-registry");
            File.WriteAllText(Path.Combine(state, "transactions", "pending.json"), "recovery-data");
            var snapshot = UpgradeBackup.Ensure(state, Path.Combine(root, "config.xml"), "0.7.2.0");
            Assert.Equal("private-config-test-only", File.ReadAllText(Path.Combine(snapshot, "configuration.xml")));
            Assert.Equal("original-registry", File.ReadAllText(Path.Combine(snapshot, "state", "downloads.json")));
            Assert.Equal("previous-registry", File.ReadAllText(Path.Combine(snapshot, "state", "downloads.json.bak")));
            Assert.Equal("recovery-data", File.ReadAllText(Path.Combine(snapshot, "state", "transactions", "pending.json")));
            File.WriteAllText(Path.Combine(state, "downloads.json"), "changed-after-upgrade");
            Assert.Equal(snapshot, UpgradeBackup.Ensure(state, Path.Combine(root, "config.xml"), "0.7.2.0"));
            Assert.Equal("original-registry", File.ReadAllText(Path.Combine(snapshot, "state", "downloads.json")));
        });
    }

    [Fact]
    public void TamperedBackupStopsStartupWithoutTouchingOriginalState()
    {
        WithDirectory(root =>
        {
            var state = Path.Combine(root, "AzureIllusion");
            Directory.CreateDirectory(state);
            File.WriteAllText(Path.Combine(state, "downloads.json"), "original");
            var config = Path.Combine(root, "config.xml");
            var snapshot = UpgradeBackup.Ensure(state, config, "0.7.2.0");
            File.WriteAllText(Path.Combine(snapshot, "state", "downloads.json"), "damaged");
            Assert.Throws<InvalidDataException>(() => UpgradeBackup.Ensure(state, config, "0.7.2.0"));
            Assert.Equal("original", File.ReadAllText(Path.Combine(state, "downloads.json")));
        });
    }

    [Fact]
    public void UnreadableRegistryNeverCreatesCompletedSnapshot()
    {
        WithDirectory(root =>
        {
            var state = Path.Combine(root, "AzureIllusion");
            Directory.CreateDirectory(state);
            var path = Path.Combine(state, "downloads.json");
            File.WriteAllText(path, "original");
            using (var locked = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                Assert.ThrowsAny<IOException>(() => UpgradeBackup.Ensure(state, Path.Combine(root, "config.xml"), "0.7.2.0"));
            Assert.False(Directory.Exists(Path.Combine(state + "-backups", "0.7.2.0")));
            Assert.Equal("original", File.ReadAllText(path));
        });
    }

    [Fact]
    public void InterruptedStagingDoesNotPreventFreshVerifiedSnapshot()
    {
        WithDirectory(root =>
        {
            var state = Path.Combine(root, "AzureIllusion");
            var pending = Path.Combine(state + "-backups", "0.7.2.0.pending-interrupted");
            Directory.CreateDirectory(pending);
            File.WriteAllText(Path.Combine(pending, "partial"), "preserve");
            var snapshot = UpgradeBackup.Ensure(state, Path.Combine(root, "absent.xml"), "0.7.2.0");
            Assert.True(File.Exists(Path.Combine(snapshot, "manifest.json")));
            Assert.True(File.Exists(Path.Combine(pending, "partial")));
        });
    }

    [Theory]
    [InlineData("../outside")]
    [InlineData("..")]
    [InlineData("")]
    public void UnsafeVersionCannotEscapeBackupDirectory(string version)
        => Assert.Throws<ArgumentException>(() => UpgradeBackup.Ensure("state", "config", version));

    private static void WithDirectory(Action<string> action)
    {
        var root = Path.Combine(Path.GetTempPath(), "pna-upgrade-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try { action(root); }
        finally { Directory.Delete(root, true); }
    }
}
