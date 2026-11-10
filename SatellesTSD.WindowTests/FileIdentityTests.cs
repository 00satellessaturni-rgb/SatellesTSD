using System.Runtime.InteropServices;

namespace SatellesTSD.WindowTests;

/// <summary>One file under two spellings is one store: the remembered store named on the command line another way
/// (a mapped drive and its network path, a short 8.3 name) is no one-off (decision 147).</summary>
public class FileIdentityTests
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateHardLinkW(string newFile, string existingFile, IntPtr security);

    [Fact]
    public void Two_spellings_of_one_file_are_the_same_and_two_files_are_not()
    {
        var folder = Path.Combine(Path.GetTempPath(), $"satellestsd-identity-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        try
        {
            var store = Path.Combine(folder, "pood.db");
            var other = Path.Combine(folder, "teine.db");
            File.WriteAllText(store, "a");
            File.WriteAllText(other, "a");

            Assert.True(FileIdentity.Same(store, store.ToUpperInvariant()));
            Assert.True(FileIdentity.Same(store, Path.Combine(folder, "x", "..", "pood.db")));
            Assert.False(FileIdentity.Same(store, other)); // the same content is not the same file
            Assert.False(FileIdentity.Same(store, null));
            Assert.False(FileIdentity.Same(store, Path.Combine(folder, "puudub.db"))); // a missing file is nobody's twin
            Assert.True(FileIdentity.Same(Path.Combine(folder, "puudub.db"), Path.Combine(folder, "PUUDUB.db"))); // but its own spelling is

            // another name for the very file — as a mapped drive and a network path are: the spellings differ, the file is one
            var link = Path.Combine(folder, "link.db");
            Assert.True(CreateHardLinkW(link, store, IntPtr.Zero), "the hard link could not be made");
            Assert.True(FileIdentity.Same(store, link));
            using (File.Open(store, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                // a store held by another program: the identity cannot be read, and the spellings alone decide
                Assert.False(FileIdentity.Same(store, link));
            }
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
