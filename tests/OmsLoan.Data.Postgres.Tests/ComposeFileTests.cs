using System.Text.RegularExpressions;

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
    public void NoPasswordIsAskedFor()
    {
        var compose = Compose();

        Assert.Matches(@"(?m)^\s+POSTGRES_HOST_AUTH_METHOD:\s*trust\s*$", compose);
        Assert.DoesNotContain("POSTGRES_PASSWORD", compose, StringComparison.Ordinal);
    }

    [Fact]
    public void ThePortIsReachableFromThisMachineOnly()
    {
        var mappings = Regex.Matches(Compose(), @"(?m)^\s+-\s*""?(?<mapping>[\d.:]+:5432)""?\s*$")
            .Select(match => match.Groups["mapping"].Value);

        Assert.Equal("127.0.0.1:5432:5432", Assert.Single(mappings));
    }

    [Fact]
    public void TheVolumeNameDoesNotDependOnTheCheckoutFolder() =>
        Assert.Matches(@"(?m)^\s+omsloan-postgres:\s*\r?\n\s+name:\s*omsloan-postgres\s*$", Compose());
}
