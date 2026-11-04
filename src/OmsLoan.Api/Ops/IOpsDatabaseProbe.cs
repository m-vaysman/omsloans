namespace OmsLoan.Api.Ops;

// Seam phase 2 fills with CanConnectAsync.
public interface IOpsDatabaseProbe
{
    Task<string> ProbeAsync(CancellationToken cancellationToken);
}

// Phase 1 measures nothing, so it answers NotMeasured rather than Healthy. A
// stub that claims health is a lie any JSON consumer would believe.
public sealed class StubOpsDatabaseProbe : IOpsDatabaseProbe
{
    public static readonly StubOpsDatabaseProbe Instance = new();

    public Task<string> ProbeAsync(CancellationToken cancellationToken) =>
        Task.FromResult(OpsDatabaseState.NotMeasured);
}
