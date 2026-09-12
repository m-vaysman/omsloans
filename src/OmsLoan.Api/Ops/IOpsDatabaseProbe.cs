namespace OmsLoan.Api.Ops;

public interface IOpsDatabaseProbe
{
    Task<string> ProbeAsync(CancellationToken cancellationToken);
}

public sealed class StubOpsDatabaseProbe : IOpsDatabaseProbe
{
    public static readonly StubOpsDatabaseProbe Instance = new();

    public Task<string> ProbeAsync(CancellationToken cancellationToken) =>
        Task.FromResult(OpsDatabaseState.Healthy);
}
