using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using OmsLoan.Domain;

namespace OmsLoan.Worker.Ingestion;

/// <summary>
/// One pass over the watched folder: read each file, record it, then move it.
/// </summary>
/// <remarks>
/// <para>
/// The whole design is one rule — <strong>a file is never moved until its row is
/// committed</strong> — and everything else follows from it.
/// </para>
/// <para>
/// It makes the database the commit point and the watched folder the queue. If the database
/// is unavailable, files simply accumulate where they were dropped and are picked up when it
/// returns; nothing needs replaying by hand, and an outage shows up as a folder filling
/// rather than as notices that quietly never existed. It is also why the Worker does not
/// refuse to start when the database is unreachable.
/// </para>
/// <para>
/// The cost is that recording and moving are not atomic, so a crash or a failed move between
/// them means the file is read again next poll and recorded a second time. That is
/// at-least-once, and it is the right way round: a duplicate row is recoverable, a lost
/// notice is not. It is also why <c>Notices.Sha256</c> is no longer uniquely indexed — with
/// a unique index the retry would throw, the file would never move, and it would be retried
/// forever.
/// </para>
/// <para>
/// No deduplication happens here. Deciding that two arrivals are the same document is
/// review's job; ingestion logs, reads, records and moves.
/// </para>
/// </remarks>
public sealed class FolderIngestion(
    INoticeStore store,
    IOptions<IngestionOptions> options,
    ILogger<FolderIngestion> logger,
    TimeProvider? timeProvider = null)
{
    private readonly IngestionOptions _options = options.Value;
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;

    /// <summary>
    /// Consecutive read failures per file, so a file that can never be read eventually stops
    /// being retried. Held in memory only: a restart granting a fresh set of attempts is the
    /// right behaviour, since a restart is often exactly what fixed the problem.
    /// </summary>
    private readonly Dictionary<string, int> _readAttempts = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The first four bytes of every PDF.</summary>
    private static readonly byte[] PdfMagic = "%PDF"u8.ToArray();

    public async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        var watched = _options.WatchedFolder;

        // Top directory only: processed/ and failed/ normally sit inside the watched folder,
        // and a recursive scan would ingest everything already archived, on every poll.
        List<string> files;

        try
        {
            files = [.. Directory.EnumerateFiles(watched, "*", SearchOption.TopDirectoryOnly)];
        }
        catch (Exception ex)
        {
            // The folder was checked at startup, so this is something that changed underneath
            // us — a share going away, usually. Nothing to do but say so and try again.
            logger.LogError(ex, "Could not list the watched folder {Folder}.", watched);
            return;
        }

        foreach (var file in files)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            await IngestAsync(file, cancellationToken);
        }
    }

    private async Task IngestAsync(string file, CancellationToken cancellationToken)
    {
        var name = Path.GetFileName(file);

        var content = TryRead(file, out var readFailure);

        if (content is null)
        {
            RecordReadFailure(file, name, readFailure);
            return;
        }

        _readAttempts.Remove(file);

        var hash = Sha256Hex(content);

        if (!IsPdf(content))
        {
            // Permanent, so no retries: the bytes read fine and are not a PDF. Moving it out
            // keeps the folder honest, and it is the one case besides an unreadable file
            // where something moves without a row.
            logger.LogWarning(
                "{File} (sha256 {Hash}) is not a PDF. Moving to failed.", name, hash);

            Archive(file, "failed", name);
            return;
        }

        var notice = new Notice
        {
            Content = content,
            Sha256 = hash,
            Sender = null,

            // Deliberately null. The filesystem timestamp is when the file was dropped here,
            // not when the agent bank sent it, and inventing a value would be worse than
            // admitting we do not know.
            SentAtUtc = null,

            ReceivedAtUtc = _time.GetUtcNow().UtcDateTime,
            Status = NoticeStatus.Received,
        };

        try
        {
            await store.AddAsync(notice, cancellationToken);
        }
        catch (Exception ex)
        {
            // The rule: not recorded, so not moved. It stays and is retried, indefinitely if
            // the database stays down. No attempt counter here — that would eventually move
            // aside a perfectly good notice during an outage.
            logger.LogError(
                ex,
                "{File} (sha256 {Hash}) could not be recorded. Leaving it in place to retry.",
                name,
                hash);
            return;
        }

        logger.LogInformation(
            "Ingested {File} (sha256 {Hash}) as notice {NoticeId}.", name, hash, notice.NoticeId);

        if (!Archive(file, "processed", name))
        {
            // Recorded but not moved. Next poll reads it again and records it again, which is
            // the accepted duplicate rather than a stuck file.
            logger.LogWarning(
                "{File} (sha256 {Hash}) was recorded as notice {NoticeId} but could not be moved. "
                + "It will be ingested again on the next poll, producing a duplicate row.",
                name,
                hash,
                notice.NoticeId);
        }
    }

    /// <summary>
    /// Reads the whole file, or reports why not. Opened with no sharing, so a file still
    /// being written by whoever dropped it fails here rather than being read half-copied and
    /// stored as a truncated notice.
    /// </summary>
    private static byte[]? TryRead(string file, out string? failure)
    {
        try
        {
            using var stream = new FileStream(
                file, FileMode.Open, FileAccess.Read, FileShare.None);

            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);

            failure = null;
            return buffer.ToArray();
        }
        catch (Exception ex)
        {
            failure = ex.Message;
            return null;
        }
    }

    private void RecordReadFailure(string file, string name, string? failure)
    {
        _readAttempts.TryGetValue(file, out var attempts);
        attempts++;
        _readAttempts[file] = attempts;

        if (attempts < _options.MaxReadAttempts)
        {
            // Almost always a file still being copied, so this is expected and not a warning.
            logger.LogDebug(
                "{File} could not be read (attempt {Attempt} of {Max}): {Reason}. Leaving it to retry.",
                name,
                attempts,
                _options.MaxReadAttempts,
                failure);
            return;
        }

        logger.LogWarning(
            "{File} could not be read after {Attempts} attempts: {Reason}. Moving to failed.",
            name,
            attempts,
            failure);

        if (Archive(file, "failed", name))
        {
            _readAttempts.Remove(file);
        }
    }

    /// <summary>
    /// Moves the file into an archive subfolder. Returns false rather than throwing: every
    /// caller has already decided what a failed move means, and none of them is "give up on
    /// the rest of the folder".
    /// </summary>
    private bool Archive(string file, string subfolder, string name)
    {
        var destination = Path.Combine(_options.EffectiveArchiveFolder, subfolder, name);

        // A same-named file arriving twice is ordinary — agent banks reuse filenames — so the
        // destination is made unique rather than overwritten. Overwriting would destroy the
        // evidence for a notice that is already recorded.
        if (File.Exists(destination))
        {
            var stamp = _time.GetUtcNow().ToString("yyyyMMddHHmmssfff");
            destination = Path.Combine(
                _options.EffectiveArchiveFolder,
                subfolder,
                $"{Path.GetFileNameWithoutExtension(name)}-{stamp}{Path.GetExtension(name)}");
        }

        try
        {
            File.Move(file, destination);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not move {File} to {Subfolder}.", name, subfolder);
            return false;
        }
    }

    private static bool IsPdf(byte[] content) =>
        content.Length >= PdfMagic.Length && content.AsSpan(0, PdfMagic.Length).SequenceEqual(PdfMagic);

    /// <summary>Lowercase hex, matching what is stored on <see cref="Notice.Sha256"/>.</summary>
    public static string Sha256Hex(byte[] content) =>
        Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
}
