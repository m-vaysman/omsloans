using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OmsLoan.Domain;

namespace OmsLoan.Data.Postgres;

public static class PostgresRegistration
{
    public static IServiceCollection AddOmsLoanDatabase(
        this IServiceCollection services,
        IConfiguration configuration,
        string connectionString)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var provider = configuration[DatabaseProvider.ConfigurationKey];

        if (string.IsNullOrWhiteSpace(provider)
            || provider.Equals(DatabaseProvider.SqlServer, StringComparison.OrdinalIgnoreCase))
        {
            return services.AddOmsLoanDbContext(connectionString);
        }

        if (provider.Equals(DatabaseProvider.Postgres, StringComparison.OrdinalIgnoreCase))
        {
            return services.AddOmsLoanPostgresDbContext(connectionString);
        }

        throw new InvalidOperationException(
            $"{DatabaseProvider.ConfigurationKey} is '{provider}'. "
            + $"Supported values are '{DatabaseProvider.SqlServer}' and '{DatabaseProvider.Postgres}'.");
    }

    public static IServiceCollection AddOmsLoanPostgresDbContext(
        this IServiceCollection services,
        string connectionString) =>
        services.AddDbContext<OmsLoanDbContext>(options => options.UseOmsLoanPostgres(connectionString));

    public static DbContextOptionsBuilder UseOmsLoanPostgres(
        this DbContextOptionsBuilder options,
        string connectionString)
    {
        ArgumentNullException.ThrowIfNull(options);

        return options
            .UseNpgsql(
                connectionString,
                npgsql => npgsql.MigrationsAssembly(typeof(PostgresRegistration).Assembly.GetName().Name))
            .ReplaceService<IModelCustomizer, PostgresModelCustomizer>();
    }

    public static DbContextOptionsBuilder<TContext> UseOmsLoanPostgres<TContext>(
        this DbContextOptionsBuilder<TContext> options,
        string connectionString)
        where TContext : DbContext
    {
        UseOmsLoanPostgres((DbContextOptionsBuilder)options, connectionString);
        return options;
    }
}
