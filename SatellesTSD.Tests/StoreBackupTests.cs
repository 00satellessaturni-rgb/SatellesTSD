using SatellesTSD.Infrastructure;

namespace SatellesTSD.Tests;

/// <summary>A file store from an older build is copied beside itself before this build upgrades it, once, so that the
/// older program can be put back after an update that went wrong.</summary>
public class StoreBackupTests
{
    [Fact]
    public void An_older_store_is_copied_beside_itself_before_the_upgrade_and_the_copy_is_kept()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"satellestsd-backup-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        try
        {
            var path = Path.Combine(folder, "firma.db");
            var older = SqliteStore.SchemaVersion - 1;
            using (var old = SqliteStore.OpenAtVersion(path, older))
            {
                old.AddCompany("10235688", "Näidis OÜ");
            }

            var backup = SqliteStore.BackupPathFor(path);
            Assert.EndsWith($".before-v{SqliteStore.SchemaVersion}", backup, StringComparison.Ordinal);
            Assert.False(File.Exists(backup));

            using (var upgraded = SqliteStore.Open(path))
            {
                Assert.Single(upgraded.Companies());
            }

            Assert.True(File.Exists(backup));
            using (var kept = SqliteStore.OpenAtVersion(backup, older))
            {
                Assert.Equal(older, kept.ScalarRaw<long>("SELECT MAX(version) FROM schema_version"));
                Assert.Single(kept.Companies());
            }

            // a second open (already at this version) leaves the copy alone; no half-written copy stays beside it
            var stamp = File.GetLastWriteTimeUtc(backup);
            using (SqliteStore.Open(path))
            {
            }

            Assert.Equal(stamp, File.GetLastWriteTimeUtc(backup));
            Assert.False(File.Exists(backup + ".partial"));

            // the older build finds the copy a newer build left, and puts it back with the upgraded store set aside
            Assert.Null(SqliteStore.NewerBackupOf(path)); // this build's own copy is not "newer"
            var newerCopy = $"{path}.before-v{SqliteStore.SchemaVersion + 1}";
            File.Copy(backup, newerCopy);
            File.Copy(backup, $"{path}.before-v{SqliteStore.SchemaVersion + 3}");
            Assert.Equal(newerCopy, SqliteStore.NewerBackupOf(path)); // the earliest beyond this build: the store as it last knew it
            var aside = SqliteStore.AsidePathFor(path);
            Assert.Equal(aside, SqliteStore.RestoreBackup(path, newerCopy, aside));
            Assert.True(File.Exists(aside) && File.Exists(newerCopy) && File.Exists(path));

            // a second restore never deletes what the first set aside: the store goes aside under a stamped name
            var stamp2 = File.GetLastWriteTimeUtc(aside);
            var second = SqliteStore.RestoreBackup(path, newerCopy, aside);
            Assert.NotEqual(aside, second);
            Assert.StartsWith(aside + "-", second, StringComparison.Ordinal);
            Assert.True(File.Exists(aside) && File.Exists(second) && File.Exists(path));
            Assert.Equal(stamp2, File.GetLastWriteTimeUtc(aside));
            Assert.True(SqliteStore.IsNewerThanThisBuild(new InvalidOperationException("The store is at schema version 9, newer than this build (6).")));
            Assert.False(SqliteStore.IsNewerThanThisBuild(new IOException("locked")));

            // a brand-new store leaves no copy behind
            var fresh = Path.Combine(folder, "uus.db");
            using (SqliteStore.Open(fresh))
            {
            }

            Assert.False(File.Exists(SqliteStore.BackupPathFor(fresh)));
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }
}
