using Azure.Identity;
using Microsoft.Extensions.Options;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using OmsLoan.Domain;

namespace OmsLoan.Worker.Ingestion.Email;

/// <summary>
/// <see cref="IMailboxClient"/> over Microsoft Graph, app-only.
/// </summary>
/// <remarks>
/// <para>
/// Client credentials, not delegated auth: the Worker runs unattended and a delegated token
/// tied to somebody's account stops working the moment their password rotates or they leave.
/// <c>Mail.Read</c> should be scoped to this one mailbox with an application access policy —
/// the grant is tenant-wide otherwise, and this application has no business reading anybody
/// else's mail. See docs/exchange-test-environment.md.
/// </para>
/// <para>
/// This is the only part of mailbox ingestion a unit test cannot reach, which is why it does
/// as little as possible: fetch, filter to PDFs, hand back plain records. Every rule about
/// what happens to those records lives in <see cref="EmailIngestion"/>, where it can be
/// tested.
/// </para>
/// </remarks>
public sealed class GraphMailboxClient : IMailboxClient
{
    private readonly GraphServiceClient _graph;
    private readonly MailboxOptions _options;
    private readonly ILogger<GraphMailboxClient> _logger;

    public GraphMailboxClient(IOptions<MailboxOptions> options, ILogger<GraphMailboxClient> logger)
    {
        _options = options.Value;
        _logger = logger;

        // The SDK retries 429 and 503 with Retry-After honoured, by default, through its
        // built-in retry handler. Nothing here should second-guess it: a hand-rolled backoff
        // on top would multiply the wait and hide the header Graph actually sent.
        _graph = new GraphServiceClient(
            new ClientSecretCredential(_options.TenantId, _options.ClientId, _options.ClientSecret));
    }

    public async Task<IReadOnlyList<MailboxMessage>> GetUnreadWithPdfAttachmentsAsync(
        int maxMessages,
        CancellationToken cancellationToken)
    {
        var page = await _graph.Users[_options.Mailbox].Messages.GetAsync(
            request =>
            {
                // Unread is the queue. Filtering server-side rather than fetching everything
                // and discarding matters on a mailbox with years of history in it.
                request.QueryParameters.Filter = "isRead eq false and hasAttachments eq true";
                request.QueryParameters.Top = maxMessages;
                request.QueryParameters.Select = ["id", "from", "sentDateTime", "subject"];

                // Oldest first: a backlog drains from the front, and notices are handled in
                // the order the agent banks sent them.
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

            var attachments = await GetPdfAttachmentsAsync(message.Id, cancellationToken);

            if (attachments.Count == 0)
            {
                // hasAttachments is true for inline images and signatures too, so a message
                // can match the filter and carry nothing we want. Marking it read stops it
                // being re-examined on every poll for ever.
                _logger.LogInformation(
                    "Message {MessageId} has no PDF attachments. Marking it read.", message.Id);

                await MarkReadAsync(message.Id, cancellationToken);
                continue;
            }

            messages.Add(new MailboxMessage(
                message.Id,
                message.From?.EmailAddress?.Address,
                message.SentDateTime?.UtcDateTime,
                attachments));
        }

        return messages;
    }

    public Task MarkReadAsync(string messageId, CancellationToken cancellationToken) =>
        _graph.Users[_options.Mailbox].Messages[messageId].PatchAsync(
            new Message { IsRead = true }, cancellationToken: cancellationToken);

    /// <summary>
    /// The PDF attachments of one message, judged on the bytes.
    /// </summary>
    /// <remarks>
    /// Not on <c>contentType</c> and not on the file name. A mail client sets those and gets
    /// them wrong — the same reason the upload endpoint stopped trusting the declared type.
    /// Inline images and signature logos are filtered out here by failing that check.
    /// </remarks>
    private async Task<IReadOnlyList<MailAttachment>> GetPdfAttachmentsAsync(
        string messageId,
        CancellationToken cancellationToken)
    {
        var page = await _graph.Users[_options.Mailbox].Messages[messageId].Attachments.GetAsync(
            cancellationToken: cancellationToken);

        var attachments = new List<MailAttachment>();

        foreach (var attachment in page?.Value ?? [])
        {
            // Only a real file attachment carries bytes. An item attachment is a forwarded
            // message and an inline reference is a pointer, and neither is a notice.
            if (attachment is not FileAttachment { ContentBytes: { Length: > 0 } bytes } file)
            {
                continue;
            }

            if (!NoticeContent.IsPdf(bytes))
            {
                continue;
            }

            attachments.Add(new MailAttachment(bytes, file.Name ?? "(unnamed).pdf"));
        }

        return attachments;
    }
}
