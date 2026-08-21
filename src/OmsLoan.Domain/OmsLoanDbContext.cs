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

    public DbSet<Notice> Notices => Set<Notice>();

    public DbSet<Extraction> Extractions => Set<Extraction>();

    public DbSet<ExtractedField> ExtractedFields => Set<ExtractedField>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(OmsLoanDbContext).Assembly);
    }
}
