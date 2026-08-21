using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using OmsLoan.Domain;

namespace OmsLoan.Domain.Tests;

/// <summary>
/// The built EF Core model, shared across every configuration test.
/// </summary>
/// <remarks>
/// EF builds the model lazily and without opening a connection, so the connection string
/// below is never dialled and no database is required to run these tests. It has to name
/// the SQL Server provider specifically, though: column types, index filters and precision
/// are relational annotations, and asserting them against the in-memory provider would
/// prove nothing about the database this system actually deploys to.
/// </remarks>
internal static class DomainModel
{
    private const string ModelOnlyConnectionString =
        "Server=(local);Database=OmsLoanModelOnly;Trusted_Connection=True;TrustServerCertificate=true";

    private static readonly Lazy<IModel> Built = new(() =>
    {
        var options = new DbContextOptionsBuilder<OmsLoanDbContext>()
            .UseSqlServer(ModelOnlyConnectionString)
            .Options;

        using var context = new OmsLoanDbContext(options);
        return context.Model;
    });

    public static IModel Instance => Built.Value;

    /// <summary>Resolves an entity type, failing the test with a useful message if it is not mapped.</summary>
    public static IEntityType Entity<TEntity>()
        where TEntity : class
    {
        var entityType = Instance.FindEntityType(typeof(TEntity));
        Assert.True(entityType is not null, $"{typeof(TEntity).Name} is not mapped in the model.");
        return entityType!;
    }

    /// <summary>Resolves a mapped property, failing the test if the name does not exist on the entity.</summary>
    public static IProperty Property<TEntity>(string propertyName)
        where TEntity : class
    {
        var property = Entity<TEntity>().FindProperty(propertyName);
        Assert.True(property is not null, $"{typeof(TEntity).Name}.{propertyName} is not mapped.");
        return property!;
    }

    /// <summary>The single index covering exactly <paramref name="propertyNames"/>, in order.</summary>
    public static IIndex Index<TEntity>(params string[] propertyNames)
        where TEntity : class
    {
        var matches = Entity<TEntity>()
            .GetIndexes()
            .Where(i => i.Properties.Select(p => p.Name).SequenceEqual(propertyNames))
            .ToList();

        Assert.True(
            matches.Count == 1,
            $"Expected exactly one index on {typeof(TEntity).Name}({string.Join(", ", propertyNames)}), found {matches.Count}.");

        return matches[0];
    }
}
