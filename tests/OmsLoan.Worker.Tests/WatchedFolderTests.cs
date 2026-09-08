namespace OmsLoan.Worker.Tests;

/// <summary>
/// The watched folder check: create what is missing, leave what is there, and prove the
/// account can actually read and write.
/// </summary>
public class WatchedFolderTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "omsloan-watched-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    [Fact]
    public void MissingFolderIsCreatedWithItsSubfolders()
    {
        Assert.False(Directory.Exists(_root));

        Assert.Null(WatchedFolder.Prepare(_root));

        Assert.True(Directory.Exists(_root));
        foreach (var name in WatchedFolder.Subfolders)
        {
            Assert.True(Directory.Exists(Path.Combine(_root, name)), $"{name} was not created");
        }
    }

    /// <summary>
    /// The half that matters on a real host, where the drop folder was created by somebody
    /// else long before the service account existed: existing content and the folder itself
    /// must come through untouched.
    /// </summary>
    [Fact]
    public void ExistingFolderAndItsContentsAreLeftAlone()
    {
        Directory.CreateDirectory(_root);
        var existing = Path.Combine(_root, "already-here.pdf");
        File.WriteAllText(existing, "notice");
        var writtenAt = File.GetLastWriteTimeUtc(existing);

        Assert.Null(WatchedFolder.Prepare(_root));

        Assert.True(File.Exists(existing));
        Assert.Equal("notice", File.ReadAllText(existing));
        Assert.Equal(writtenAt, File.GetLastWriteTimeUtc(existing));
    }

    /// <summary>
    /// Missing subfolders are filled in even when the root is already there — the usual
    /// state of a host on its first deployment.
    /// </summary>
    [Fact]
    public void SubfoldersAreAddedToAnExistingRoot()
    {
        Directory.CreateDirectory(_root);
        Directory.CreateDirectory(Path.Combine(_root, "processed"));

        Assert.Null(WatchedFolder.Prepare(_root));

        foreach (var name in WatchedFolder.Subfolders)
        {
            Assert.True(Directory.Exists(Path.Combine(_root, name)));
        }
    }

    /// <summary>
    /// Running twice must be as harmless as running once: every restart calls this.
    /// </summary>
    [Fact]
    public void PreparingTwiceIsHarmless()
    {
        Assert.Null(WatchedFolder.Prepare(_root));
        Assert.Null(WatchedFolder.Prepare(_root));
    }

    /// <summary>
    /// No probe file may survive. One left behind per restart would accumulate in a folder
    /// ingestion is scanning, and would eventually be picked up as a notice.
    /// </summary>
    [Fact]
    public void NoProbeFileIsLeftBehind()
    {
        Assert.Null(WatchedFolder.Prepare(_root));

        var strays = Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories).ToList();

        Assert.Empty(strays);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NoConfiguredPathIsRefused(string? path)
    {
        var problem = WatchedFolder.Prepare(path);

        Assert.NotNull(problem);
        Assert.Contains("no watched folder", problem, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The reason has to name the folder. Under the SCM this message is the only record an
    /// operator gets, and "permission denied" without a path is not actionable.
    /// </summary>
    [Fact]
    public void AnUncreatableFolderIsRefusedWithAReasonNamingIt()
    {
        // A file where a directory needs to be: creation fails for a reason that has nothing
        // to do with ACLs, so this runs anywhere, including CI containers running as root.
        var blocker = Path.Combine(_root, "blocked");
        Directory.CreateDirectory(_root);
        File.WriteAllText(blocker, "not a directory");

        var problem = WatchedFolder.Prepare(blocker);

        Assert.NotNull(problem);
        Assert.Contains(blocker, problem, StringComparison.Ordinal);
    }

    [Fact]
    public void RelativePathsAreResolvedSoTheReasonNamesTheRealFolder()
    {
        var problem = WatchedFolder.Prepare("relative-path-that-should-be-resolved");

        // Created relative to the test host's working directory, so clean it up rather than
        // leaving it in the output folder.
        var created = Path.GetFullPath("relative-path-that-should-be-resolved");
        try
        {
            Assert.Null(problem);
            Assert.True(Directory.Exists(created));
            Assert.True(Path.IsPathRooted(created));
        }
        finally
        {
            if (Directory.Exists(created))
            {
                Directory.Delete(created, recursive: true);
            }
        }
    }
}
