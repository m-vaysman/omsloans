using Microsoft.EntityFrameworkCore;

namespace LoanDbModel
{
    public class LoanDbContext : DbContext
    {
        protected override void OnConfiguring(DbContextOptionsBuilder options)
      => options.UseSqlServer(@"Data Source=YOUR_SERVER;User ID=YOUR_USER;Password="";Initial Catalog=Oms;TrustServerCertificate=true;MultipleActiveResultSets=True;Max Pool Size=100;")
                      ;
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {



            base.OnModelCreating(modelBuilder);


            modelBuilder.Entity<CounterParty>()
                .ToTable("CounterParties", "loan")
                .Property(t => t.CounterPartyName)
                .HasColumnType("varchar(500)")
                .IsRequired();


            modelBuilder.Entity<Blotter>()
                .ToTable("Blotters", "loan")
                .HasOne(cp => cp.CounterParty)
                .WithMany(b => b.Blotters)
                .HasForeignKey(c => c.CounterPartyId)
                .OnDelete(DeleteBehavior.NoAction);


            modelBuilder.Entity<Trade>()
                .ToTable("Trades", "loan")
                .HasOne(t => t.CounterParty)
                .WithMany(cp => cp.Trades)
                .HasForeignKey(t => t.CounterPartyId)
                .OnDelete(DeleteBehavior.NoAction);
            //.HasConstraintName(null); // Prevents SQL FK constraint generation

            modelBuilder.Entity<Paydown>()
                .ToTable("Paydowns", "loan")
                .HasOne(t => t.Trade)
                .WithMany(t => t.Paydowns)
                .HasForeignKey(t => t.TradeId)
                .OnDelete(DeleteBehavior.NoAction);

            modelBuilder.Entity<SettledPaydownCashWire>()
                .ToTable("SettledPaydownCashWires", "loan")
                .HasOne(t => t.Paydown)
                .WithMany(t => t.SettledPaydownCashWires)
                .HasForeignKey(t => t.PaydownId)
                .OnDelete(DeleteBehavior.NoAction);
        }

        public DbSet<Blotter> BlotteredTrades { get; set; }
        public DbSet<Trade> Trades { get; set; }
        public DbSet<Paydown> Paydowns { get; set; }
        public DbSet<Accrual> Accruals { get; set; }

        public DbSet<CounterParty> CounterParties { get; set; }
        public DbSet<SettledPaydownCashWire> SettledPaydownCashWires { get; set; }
    }
}
