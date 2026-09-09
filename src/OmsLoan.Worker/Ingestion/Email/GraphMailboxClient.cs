using Azure.Identity;
using Microsoft.Extensions.Options;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Graph.Users.Item.Messages.Item.Move;
using Microsoft.Kiota.Abstractions;
using OmsLoan.Domain;

namespace OmsLoan.Worker.Ingestion.Email;

/// <summary>
/// <see cref="IMailboxClient"/> over Microsoft Graph, app-only.
/// </summary>
/// <remarks>
/// <para>
/// Client credentials, not delegated auth: the Worker runs unattended and a delegated token
/// tied to somebody's account stops working the moment their password rotates or they leave.
/// <c>Mail.ReadWrite</c> — reading is only half the job, and marking a message read is a
/// write — should be scoped to this one mailbox with an application access policy;
/// the grant is tenant-wide otherwise, and this application has no business reading anybody
/// else's mail. See docs/exchange-test-environment.md.
/// </para>
/// <para>
/// This is the only part of mailbox ingestion a unit test cannot reach, which is why it does
/// as little as possible: fetch, filter to PDFs, hand back plain records. It has no side
/// effects — nothing here marks anything read. Every rule about what happens to those records
/// lives in <see cref="EmailIngestion"/>, where it can be tested.
/// </para>
/// </remarks>
public sealed class GraphMailboxClient : IMailboxClient
{
    /// <summary>
    /// Lower bound on <c>sentDateTime</c>, present only to satisfy Graph's filter rules.
    /// </summary>
    /// <remarks>
    /// Graph refuses <c>$orderby</c> on a property that does not also appear in
    /// <c>$filter</c>, and rejects the request with <c>InefficientFilter</c>. Ordering by
    /// <c>sentDateTime</c> therefore requires filtering on it, so this is an open bound that
    /// excludes nothing — no notice predates it.
    ///
    /// Dropping the <c>$orderby</c> instead would have been simpler and wrong: Graph's default
    /// order is newest first, so a backlog larger than one page would never drain from the
    /// front. The oldest notices would sit unread indefinitely while newer ones jumped them.
    /// </remarks>
    private const string SentDateTimeLowerBound = "1900-01-01T00:00:00Z";

    private readonly GraphServiceClient _graph;
    private readonly MailboxOptions _options;
    private readonly ILogger<GraphMailboxClient> _logger;

    /// <summary>
    /// Id of the processed folder, resolved once. Folders do not move and the Worker is the
    /// only thing creating this one, so looking it up on every message would be a round trip
    /// per notice for an answer that never changes.
    /// </summary>
    private string? _processedFolderId;

    public GraphMailboxClient(IOptions<MailboxOptions> options, ILogger<GraphMailboxClient> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _options = options.Value;
        _logger = logger;

        ArgumentException.ThrowIfNullOrWhiteSpace(_options.Mailbox, "options.Value.Mailbox");

        // The SDK retries 429 and 503 with Retry-After honoured, through its built-in retry
        // handler. Nothing here second-guesses it: a hand-rolled backoff on top would multiply
        // the wait and hide the header Graph actually sent.
        _graph = new GraphServiceClient(
            new ClientSecretCredential(_options.TenantId, _options.ClientId, _options.ClientSecret));
    }

    public async Task<IReadOnlyList<MailboxMessage>> GetInboxMessagesAsync(
        int maxMessages,
        CancellationToken cancellationToken)
    {
        // The inbox, not the whole mailbox: moving a message out of it is what dequeues, so
        // the processed folder must not be searched or every notice would be found again for
        // ever.
        var page = await _graph.Users[_options.Mailbox].MailFolders["inbox"].Messages.GetAsync(
            request =>
            {
                // sentDateTime first, because Graph requires every $orderby property to appear
                // in $filter ahead of the rest. No isRead clause: somebody opening the mailbox
                // to look at a notice must not dequeue it by accident.
                request.QueryParameters.Filter =
                    $"sentDateTime ge {SentDateTimeLowerBound} and hasAttachments eq true";
                request.QueryParameters.Top = maxMessages;
                request.QueryParameters.Select = ["id", "from", "sentDateTime", "subject"];
                request.QueryParameters.Orderby = ["sentDateTime asc"];
            },
            cancellationToken);

        var messages = new List<MailboxMessage>();

        foreach (var message in page?.Value ?? [])
        {
            if (message.Id is null)
            {
                continue;
            }

            var (attachments, incomplete) = await GetPdfAttachmentsAsync(message.Id, cancellationToken);

            messages.Add(new MailboxMessage(
                message.Id,
                message.From?.EmailAddress?.Address,
                message.SentDateTime?.UtcDateTime,
                attachments,
                incomplete));
        }

        return messages;
    }

    public async Task MoveToProcessedAsync(string messageId, CancellationToken cancellationToken)
    {
        var folderId = await GetOrCreateProcessedFolderAsync(cancellationToken);

        await _graph.Users[_options.Mailbox].Messages[messageId].Move.PostAsync(
            new MovePostRequestBody { DestinationId = folderId },
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// The processed folder, created on first use if it is not there.
    /// </summary>
    /// <remarks>
    /// Created rather than required, so a new mailbox works without anybody preparing it —
    /// the same reasoning as the watched folder, which the Worker also creates rather than
    /// expecting. A failure here surfaces as a failed move, which leaves the message in the
    /// inbox: nothing is lost, it simply does not dequeue until the folder can be made.
    /// </remarks>
    private async Task<string> GetOrCreateProcessedFolderAsync(CancellationToken cancellationToken)
    {
        if (_processedFolderId is not null)
        {
            return _processedFolderId;
        }

        var existing = await _graph.Users[_options.Mailbox].MailFolders.GetAsync(
            request => request.QueryParameters.Filter =
                $"displayName eq '{_options.ProcessedFolder.Replace("'", "''", StringComparison.Ordinal)}'",
            cancellationToken);

        var folder = existing?.Value?.FirstOrDefault();

        if (folder?.Id is null)
        {
            _logger.LogInformation(
                "Creating mail folder {Folder} in {Mailbox}.", _options.ProcessedFolder, _options.Mailbox);

            folder = await _graph.Users[_options.Mailbox].MailFolders.PostAsync(
                new MailFolder { DisplayName = _options.ProcessedFolder },
                cancellationToken: cancellationToken);
        }

        _processedFolderId = folder?.Id
            ?? throw new InvalidOperationException(
                $"Could not find or create the mail folder '{_options.ProcessedFolder}'.");

        return _processedFolderId;
    }

    /// <summary>
    /// Every PDF attachment of one message, judged on the bytes, following pagination.
    /// </summary>
    /// <remarks>
    /// Not on <c>contentType</c> and not on the file name. A mail client sets those and gets
    /// them wrong — the same reason the upload endpoint stopped trusting the declared type.
    /// Inline images and signature logos are filtered out here by failing that check.
    ///
    /// Returns whether anything was missed as well as what was found. A caller that cannot
    /// tell "no PDFs" from "could not read the PDFs" will mark a lost notice as handled.
    /// </remarks>
    private async Task<(IReadOnlyList<MailAttachment> Attachments, bool Incomplete)> GetPdfAttachmentsAsync(
        string messageId,
        CancellationToken cancellationToken)
    {
        var attachments = new List<MailAttachment>();
        var incomplete = false;

        AttachmentCollectionResponse? page;

        try
        {
            page = await _graph.Users[_options.Mailbox].Messages[messageId].Attachments.GetAsync(
                cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            // Reported rather than thrown: one unreadable message must not abandon the rest of
            // the page, and it must not be mistaken for the mailbox being unreachable.
            _logger.LogWarning(ex, "Could not list attachments on message {MessageId}.", messageId);
            return ([], true);
        }

        while (page is not null)
        {
            foreach (var attachment in page.Value ?? [])
            {
                // Only a real file attachment carries bytes. An item attachment is a forwarded
                // message and a reference attachment is a link, and neither is a notice.
                if (attachment is not FileAttachment file)
                {
                    continue;
                }

                var content = file.ContentBytes;

                if (content is null || content.Length == 0)
                {
                    // Graph does not always inline the bytes — larger attachments come back
                    // with contentBytes absent and have to be fetched from $value. Treating
                    // that as "not a PDF" would drop a real notice and, worse, let the message
                    // be marked read as though it had been handled.
                    if (file.Size is null or 0)
                    {
                        continue;
                    }

                    content = await DownloadAsync(messageId, file, cancellationToken);

                    if (content is null)
                    {
                        incomplete = true;
                        continue;
                    }
                }

                if (!NoticeContent.IsPdf(content))
                {
                    continue;
                }

                attachments.Add(new MailAttachment(content, file.Name ?? "(unnamed).pdf"));
            }

            if (string.IsNullOrEmpty(page.OdataNextLink))
            {
                break;
            }

            try
            {
                page = await _graph.Users[_options.Mailbox].Messages[messageId].Attachments
                    .WithUrl(page.OdataNextLink)
                    .GetAsync(cancellationToken: cancellationToken);
            }
            catch (Exception ex)
            {
                // A later page failed. What was read is still good, but the message is not
                // complete and must not be marked read on the strength of it.
                _logger.LogWarning(
                    ex, "Could not read a later attachment page on message {MessageId}.", messageId);

                incomplete = true;
                break;
            }
        }

        return (attachments, incomplete);
    }

    /// <summary>
    /// Fetches an attachment's bytes from <c>$value</c>, for the case where Graph did not
    /// inline them. Returns null when it cannot, which the caller treats as incomplete rather
    /// than absent.
    /// </summary>
    private async Task<byte[]?> DownloadAsync(
        string messageId,
        FileAttachment file,
        CancellationToken cancellationToken)
    {
        if (file.Id is null)
        {
            return null;
        }

        try
        {
            // Built by hand against the request adapter: the generated builder for an
            // attachment has no member for $value in this SDK version, and $value is the only
            // way to get bytes Graph chose not to inline.
            var request = new RequestInformation
            {
                HttpMethod = Method.GET,
                UrlTemplate = "{+baseurl}/users/{user%2Did}/messages/{message%2Did}/attachments/{attachment%2Did}/$value",
                PathParameters = new Dictionary<string, object>
                {
                    ["baseurl"] = _graph.RequestAdapter.BaseUrl ?? "https://graph.microsoft.com/v1.0",
                    ["user%2Did"] = _options.Mailbox,
                    ["message%2Did"] = messageId,
                    ["attachment%2Did"] = file.Id,
                },
            };

            var stream = await _graph.RequestAdapter.SendPrimitiveAsync<Stream>(
                request, cancellationToken: cancellationToken);

            if (stream is null)
            {
                return null;
            }

            await using (stream)
            {
                using var buffer = new MemoryStream();
                await stream.CopyToAsync(buffer, cancellationToken);

                return buffer.ToArray();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Could not download attachment {AttachmentName} on message {MessageId}.",
                file.Name,
                messageId);

            return null;
        }
    }
}
