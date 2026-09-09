using Microsoft.Extensions.Configuration;
using OmsLoan.Worker.Ingestion.Email;

namespace OmsLoan.Worker.Tests;

/// <summary>
/// The options are init-only, which raises a fair question: does configuration binding still
/// work? It does — <c>init</c> is a compile-time restriction and the binder sets properties
/// by reflection — but that is exactly the kind of claim worth a test rather than a comment,
/// because it fails silently if it is wrong. Every property would simply keep its default and
/// the Worker would poll nothing, reporting no error at all.
/// </summary>
public class MailboxOptionsTests
{
    private static MailboxOptions Bind(Dictionary<string, string?> values)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var options = new MailboxOptions();

        configuration.GetSection(MailboxOptions.SectionName).Bind(options);

        return options;
    }

    [Fact]
    public void EveryPropertyBindsFromConfigurationDespiteBeingInitOnly()
    {
        var options = Bind(new Dictionary<string, string?>
        {
            ["Graph:TenantId"] = "tenant",
            ["Graph:ClientId"] = "client",
            ["Graph:ClientSecret"] = "secret",
            ["Graph:Mailbox"] = "notices@example.test",
            ["Graph:PollIntervalSeconds"] = "15",
            ["Graph:MessagesPerPoll"] = "5",
        });

        Assert.Equal("tenant", options.TenantId);
        Assert.Equal("client", options.ClientId);
        Assert.Equal("secret", options.ClientSecret);
        Assert.Equal("notices@example.test", options.Mailbox);
        Assert.Equal(15, options.PollIntervalSeconds);
        Assert.Equal(5, options.MessagesPerPoll);
    }

    [Fact]
    public void PollIntervalIsDerivedFromTheSeconds()
    {
        var options = Bind(new Dictionary<string, string?> { ["Graph:PollIntervalSeconds"] = "90" });

        Assert.Equal(TimeSpan.FromMinutes(1.5), options.PollInterval);
    }

    [Fact]
    public void AnAbsentSectionLeavesTheDefaults()
    {
        var options = Bind([]);

        Assert.Equal(60, options.PollIntervalSeconds);
        Assert.Equal(25, options.MessagesPerPoll);
        Assert.Equal(string.Empty, options.Mailbox);
    }
}
