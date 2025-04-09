using Microsoft.EntityFrameworkCore;

namespace LoanDbModel
{
    public class LoanDbContext : DbContext
    {
        protected override void OnConfiguring(DbContextOptionsBuilder options)
        {
            options.UseSqlServer(
                @"Data Source=YOUR_SERVER;
                  User ID=YOUR_USER;
                  Password=YOUR_PASSWORD;
                  Initial Catalog=Oms;
                  TrustServerCertificate=true;
                  MultipleActiveResultSets=True;
                  Max Pool Size=100;");
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // CounterParty
            modelBuilder.Entity<CounterParty>()
                .ToTable("CounterParties", "loans")
                .Property(t => t.CounterPartyName)
                .HasColumnType("varchar(500)")
                .IsRequired();

            // Blotter
            modelBuilder.Entity<Blotter>()
                .ToTable("Blotters", "loans")
                .HasOne(cp => cp.CounterParty)
                .WithMany(b => b.Blotters)
                .HasForeignKey(c => c.CounterPartyId)
                .OnDelete(DeleteBehavior.NoAction);

            // Trade
            modelBuilder.Entity<Trade>()
                .ToTable("Trades", "loans")
                .HasOne(t => t.CounterParty)
                .WithMany(cp => cp.Trades)
                .HasForeignKey(t => t.CounterPartyId)
                .OnDelete(DeleteBehavior.NoAction);
            // .HasConstraintName(null); // Uncomment to suppress FK constraint generation

            // Paydown
            modelBuilder.Entity<Paydown>()
                .ToTable("Paydowns", "loans")
                .HasOne(t => t.Trade)
                .WithMany(t => t.Paydowns)
                .HasForeignKey(t => t.TradeId)
                .OnDelete(DeleteBehavior.NoAction);

            // SettledPaydownCashWire
            modelBuilder.Entity<SettledPaydownCashWire>()
                .ToTable("SettledPaydownCashWires", "loans")
                .HasOne(t => t.Paydown)
                .WithMany(t => t.SettledPaydownCashWires)
                .HasForeignKey(t => t.PaydownId)
                .OnDelete(DeleteBehavior.NoAction);

            // TradeDocument
            modelBuilder.Entity<TradeDocument>()
                .ToTable("TradeDocuments", "loans");

            modelBuilder.Entity<TradeDocument>()
                .Property(p => p.ContentType)
                .HasColumnType("varchar(100)")
                .IsRequired();

            modelBuilder.Entity<TradeDocument>()
                .Property(p => p.Data)
                .HasColumnType("varbinary(max)")
                .IsRequired();

            modelBuilder.Entity<TradeDocument>()
                .Property(p => p.FileName)
                .HasColumnType("varchar(500)")
                .IsRequired();

            modelBuilder.Entity<TradeDocument>()
                .HasOne(t => t.Trade)
                .WithMany(t => t.TradeDocuments)
                .HasForeignKey(t => t.TradeId)
                .OnDelete(DeleteBehavior.NoAction);

            // Accrual
            modelBuilder.Entity<Accrual>()
                .ToTable("Accruals", "loans");

            modelBuilder.Entity<Accrual>()
                .Property(p => p.AccrualCode)
                .HasComputedColumnSql("'ACR' + RIGHT('00000' + CAST([AccrualId] AS VARCHAR(5)), 5)", stored: true)
                .HasMaxLength(30);

            modelBuilder.Entity<Accrual>()
                .HasIndex(t => t.AccrualCode)
                .IsUnique();

            modelBuilder.Entity<Accrual>()
                .HasOne(p => p.Trade)
                .WithMany(p => p.Accruals)
                .HasForeignKey(p => p.TradeId)
                .OnDelete(DeleteBehavior.NoAction);

            modelBuilder.Entity<Accrual>()
                .HasOne(p => p.ParentAccrual)
                .WithMany(p => p.ChildAccruals)
                .HasForeignKey(p => p.ParentAccrualId)
                .OnDelete(DeleteBehavior.NoAction);

            foreach (var entityType in modelBuilder.Model.GetEntityTypes())
            {
                modelBuilder.Entity(entityType.Name).Property<DateTime>("CreatedDate")
                    .HasDefaultValueSql("GETUTCDATE()");

                modelBuilder.Entity(entityType.Name).Property<DateTime>("UpdatedDate");
            }
        }

        // DbSets
        public DbSet<Blotter> BlotteredTrades { get; set; }
        public DbSet<Trade> Trades { get; set; }
        public DbSet<Paydown> Paydowns { get; set; }
        public DbSet<Accrual> Accruals { get; set; }
        public DbSet<CounterParty> CounterParties { get; set; }
        public DbSet<SettledPaydownCashWire> SettledPaydownCashWires { get; set; }
        public DbSet<TradeDocument> TradeDocuments { get; set; }

        public override int SaveChanges()
        {
            var entries = ChangeTracker.Entries()
                .Where(e => e.State == EntityState.Added || e.State == EntityState.Modified);

            foreach (var entry in entries)
            {
                if (entry.State == EntityState.Added)
                {
                    entry.Property("CreatedDate").CurrentValue = DateTime.UtcNow;
                }

                entry.Property("UpdatedDate").CurrentValue = DateTime.UtcNow;
            }

            return base.SaveChanges();
        }
    }
}
