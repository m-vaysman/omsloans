<Query Kind="Program">
  <NuGetReference>Azure.Identity</NuGetReference>
  <NuGetReference>Microsoft.Graph</NuGetReference>
  <Namespace>Azure.Identity</Namespace>
  <Namespace>Microsoft.Graph</Namespace>
  <Namespace>Microsoft.Graph.Models</Namespace>
  <Namespace>Microsoft.Graph.Users.Item.SendMail</Namespace>
  <Namespace>System.Threading.Tasks</Namespace>
</Query>

// ============================================================================
// MOCK ENVIRONMENT SMOKE TEST — not production code, and not a production path.
// ============================================================================
//
// WHAT THIS IS FOR
//
// In production, agent banks email loan notices to a shared mailbox and the Worker reads
// them out of it. There are no agent banks in the test tenant and no real notices, so this
// script stands up a mock of that arrangement: it plays the agent bank AND the Worker
// against a throwaway Microsoft 365 test mailbox.
//
// The point is to prove the Graph app-only path works before anything depends on it, and to
// give us a mailbox we can feed mock notices into — the generated PDFs in scripts/Notices —
// arriving exactly as real notices would in production.
//
// WHICH HALF IS REAL
//
//   Send  Mock only. Scaffolding. In production the notices come from agent banks and this
//         system never sends mail. Mail.Send is granted in the test tenant purely so we can
//         simulate arrivals.
//
//   Read  The production path. This is what the Worker's mailbox ingestion (issue #6) will
//         do for real: enumerate messages, pull PDF attachments, and capture the sender and
//         sent date as provenance.
//
// Every mailbox, tenant, borrower and agent bank involved is fake. No real notice,
// counterparty or account is ever handled here.
//
// CREDENTIALS
//
// Nothing identifying is hardcoded. The tenant and mailbox come from environment variables;
// the client id and secret come from LINQPad's password manager, which stores them outside
// this file. See docs/exchange-test-environment.md.

async Task Main()
{
	// ---- config -------------------------------------------------------------
	var tenantId = RequiredEnv("GRAPH_TENANT_ID");
	var clientId = Util.GetPassword("loan_notices_clientId");     // prompts once, stored in LINQPad's password manager
	var clientSecret = Util.GetPassword("loan_notices_secret");   // paste the secret VALUE here on first run
	var user = RequiredEnv("GRAPH_USER");
	// -------------------------------------------------------------------------

	var graph = new GraphServiceClient(new ClientSecretCredential(tenantId, clientId, clientSecret));

	// 1) send a test message to self
	await graph.Users[user].SendMail.PostAsync(new SendMailPostRequestBody
	{
		Message = new Message
		{
			Subject = $"loan_notices smoke test {DateTime.Now:HH:mm:ss}",
			Body = new ItemBody { ContentType = BodyType.Text, Content = "graph daemon works" },
			ToRecipients = new List<Recipient>
			{
				new Recipient { EmailAddress = new EmailAddress { Address = user } }
			}
		},
		SaveToSentItems = true
	});
	"Sent.".Dump();

	// give Exchange a moment to deliver to the inbox
	await Task.Delay(5000);

	// 2) read the newest messages back
	var msgs = await graph.Users[user].Messages.GetAsync(c =>
	{
		c.QueryParameters.Top = 10;
		c.QueryParameters.Select = new[] { "subject", "receivedDateTime", "from", "isRead" };
		c.QueryParameters.Orderby = new[] { "receivedDateTime desc" };
	});

	msgs.Value
		.Select(m => new
		{
			m.ReceivedDateTime,
			m.Subject,
			From = m.From?.EmailAddress?.Address,
			m.IsRead
		})
		.Dump("Inbox (newest first)");
}

// Fails loudly and by name rather than letting a null reach Graph, where it surfaces as an
// opaque auth error several frames down.
static string RequiredEnv(string name) =>
	Environment.GetEnvironmentVariable(name)
		?? throw new InvalidOperationException(
			$"Environment variable {name} is not set. See docs/exchange-test-environment.md.");
