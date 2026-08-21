using Microsoft.EntityFrameworkCore;

namespace OmsLoan.Domain;

/// <summary>
/// The single EF Core context for the notice-extraction system. It lives in Domain so that
/// the Worker can write extractions and the Api can serve them without either referencing
/// the other (see docs/decisions/0002-windows-service-over-desktop.md).
/// </summary>
public class OmsLoanDbContext : DbContext
{
    public OmsLoanDbContext(DbContextOptions<OmsLoanDbContext> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Entities and their IEntityTypeConfiguration<T> classes are added by the EF model issue.
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(OmsLoanDbContext).Assembly);
    }
}
