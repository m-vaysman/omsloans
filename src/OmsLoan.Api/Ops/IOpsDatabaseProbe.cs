namespace OmsLoan.Api.Ops;

// Seam phase 2 fills with CanConnectAsync.
public interface IOpsDatabaseProbe
{
    Task<string> ProbeAsync(CancellationToken cancellationToken);
}

// Phase 1 always answers Healthy; the page marks those cards stub so a
// down database is not painted green.
public sealed class StubOpsDatabaseProbe : IOpsDatabaseProbe
{
    public static readonly StubOpsDatabaseProbe Instance = new();

    public Task<string> ProbeAsync(CancellationToken cancellationToken) =>
        Task.FromResult(OpsDatabaseState.Healthy);
}
