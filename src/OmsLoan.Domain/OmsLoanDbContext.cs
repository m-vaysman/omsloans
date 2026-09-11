using Microsoft.EntityFrameworkCore;

namespace OmsLoan.Domain;

// Lives in Domain so the Worker can write extractions and the Api can serve them
// without either referencing the other (docs/decisions/0002-windows-service-over-desktop.md).
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
