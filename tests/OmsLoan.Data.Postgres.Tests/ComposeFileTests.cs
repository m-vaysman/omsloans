namespace OmsLoan.Data.Postgres.Tests;

public class ComposeFileTests
{
    private static string Compose()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "OmsLoan.sln")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);

        return File.ReadAllText(Path.Combine(directory.FullName, "docker-compose.yml"));
    }

    [Fact]
    public void ThePasswordIsRequired() =>
        Assert.Contains("${POSTGRES_PASSWORD:?", Compose(), StringComparison.Ordinal);

    [Fact]
    public void TheVolumeNameDoesNotDependOnTheCheckoutFolder() =>
        Assert.Matches(@"(?m)^\s+omsloan-postgres:\s*\r?\n\s+name:\s*omsloan-postgres\s*$", Compose());
}
