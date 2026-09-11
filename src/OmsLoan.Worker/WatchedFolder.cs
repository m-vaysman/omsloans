namespace OmsLoan.Worker;

/// <summary>
/// Prepares the watched folder at startup: create what is missing, prove the service account
/// can read and write there.
/// </summary>
/// <remarks>
/// <para>
/// Existence is not the useful question. <c>Directory.CreateDirectory</c> is a no-op on a
/// folder that already exists — and returns happily for one the account cannot write a byte
/// into. That is production: the drop share is usually created long before the service
/// account exists. Create-only would pass where it mattered and catch nothing.
/// </para>
/// <para>
/// So read, write, and delete are probed on the folder and each subfolder. Enumerate for
/// read; create-and-delete for write and delete — ingestion moves files between these folders.
/// </para>
/// <para>
/// Failure stops the service like a missing env var: clean stop, no restart. Wrong
/// permissions are not transient; 1m / 2m / 5m would fail identically.
/// </para>
/// </remarks>
public static class WatchedFolder
{
    /// <summary>
    /// Where ingestion files each notice once it has been handled. Created under the archive
/// root: that root is usually the watched folder itself, made by whoever set up the drop
    /// location, while these two are an implementation detail nobody outside the project
    /// would know to create.
    /// </summary>
    /// <remarks>
    /// No <c>duplicates</c>. Ingestion does not branch on whether it has seen a document
    /// before — every arrival is recorded, and identifying two of them as the same document
    /// is review's job — so nothing would ever be put in it.
    /// </remarks>
    public static readonly IReadOnlyList<string> Subfolders = ["processed", "failed"];

    /// <summary>
    /// Creates whatever is missing and checks read and write on each folder.
    /// Returns <c>null</c> when everything is in order, or the reason it is not.
    /// </summary>
    /// <remarks>
    /// Returns the first problem rather than collecting them all. Unlike missing environment
    /// variables — where the whole list is worth having, because they are set independently —
    /// these failures cascade: if the root cannot be created, nothing below it can either,
    /// and three more messages saying so would bury the one that matters.
    /// </remarks>
    /// <param name="root">The watched folder.</param>
    /// <param name="archiveRoot">
    /// Where the subfolders live. Blank means under <paramref name="root"/>, which is the
    /// usual arrangement.
    /// </param>
    public static string? Prepare(string? root, string? archiveRoot = null)
    {
        if (string.IsNullOrWhiteSpace(root))
        {
            return "no watched folder is configured";
        }

        string fullRoot;

        // A relative path would resolve against the working directory — for an installed
        // Windows service, wherever Windows Service Control Manager started it. Resolve here
        // so the log names the folder actually used.
        try
        {
            fullRoot = Path.GetFullPath(root);
        }
        catch (Exception ex)
        {
            return $"'{root}' is not a usable path: {ex.Message}";
        }

        var problem = PrepareOne(fullRoot);

        if (problem is not null)
        {
            return problem;
        }

        var fullArchive = fullRoot;

        if (!string.IsNullOrWhiteSpace(archiveRoot))
        {
            try
            {
                fullArchive = Path.GetFullPath(archiveRoot);
            }
            catch (Exception ex)
            {
                return $"'{archiveRoot}' is not a usable archive path: {ex.Message}";
            }

            problem = PrepareOne(fullArchive);

            if (problem is not null)
            {
                return problem;
            }
        }

        foreach (var name in Subfolders)
        {
            problem = PrepareOne(Path.Combine(fullArchive, name));

            if (problem is not null)
            {
                return problem;
            }
        }

        return null;
    }

    /// <summary>One folder: create if missing, then prove read and write.</summary>
    private static string? PrepareOne(string path)
    {
        if (!Directory.Exists(path))
        {
            try
            {
                Directory.CreateDirectory(path);
            }
            catch (Exception ex)
            {
                return $"could not create '{path}': {ex.Message}";
            }
        }

        // Enumerating is the read. Take(1) forces the lazy enumerator to touch the directory —
        // without it the call returns an object and no permission is exercised.
        try
        {
            _ = Directory.EnumerateFileSystemEntries(path).Take(1).ToList();
        }
        catch (Exception ex)
        {
            return $"cannot read '{path}': {ex.Message}";
        }

        // Write and delete. Delete is separately grantable; prove both rather than assuming
        // write implies remove.
        var probe = Path.Combine(path, $"omsloan-write-probe-{Guid.NewGuid():N}.tmp");

        try
        {
            File.WriteAllText(probe, string.Empty);
        }
        catch (Exception ex)
        {
            return $"cannot write to '{path}': {ex.Message}";
        }

        try
        {
            File.Delete(probe);
        }
        catch (Exception ex)
        {
            return $"can write to '{path}' but cannot delete: {ex.Message}";
        }

        return null;
    }
}
