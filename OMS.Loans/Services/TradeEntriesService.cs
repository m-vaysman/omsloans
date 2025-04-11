using LoanDbModel;
using Microsoft.EntityFrameworkCore;
using OMS.Loans.Common;
using System;
using System.Linq;

namespace OMS.Loans.Services
{
    /// <summary>
    /// Service for accessing and managing trade entries in the database.
    /// Implements the ITradeEntries interface.
    /// </summary>
    public class TradeEntriesService : ITradeEntries
    {
        private readonly LoanDbContext loanDbContext;

        /// <summary>
        /// Constructor that initializes the service with a database context.
        /// </summary>
        /// <param name="loanDbContext">The EF Core DbContext for loan-related data</param>
        /// <exception cref="ArgumentNullException">Thrown if context is null</exception>
        public TradeEntriesService(LoanDbContext loanDbContext)
        {
            if (loanDbContext is null)
            {
                throw new ArgumentNullException(nameof(loanDbContext));
            }

            this.loanDbContext = loanDbContext;
        }

        /// <summary>
        /// Retrieves a trade by its unique identifier.
        /// </summary>
        /// <param name="id">The TradeId to search for</param>
        /// <returns>The trade entity if found; otherwise null</returns>
        public Trade Get(int id)
        {
            return loanDbContext.Trades
                                .AsNoTracking()  // Improves performance for read-only queries
                                .FirstOrDefault(t => t.TradeId == id);
        }

        /// <summary>
        /// Saves a new trade record to the database.
        /// </summary>
        /// <param name="trade">The trade entity to insert</param>
        /// <returns>The newly generated TradeId</returns>
        public int Save(Trade trade)
        {
            loanDbContext.Trades.Add(trade);
            loanDbContext.SaveChanges();
            return trade.TradeId;
        }

        /// <summary>
        /// Updates an existing trade in the database.
        /// Note: SaveChanges() must be called by the caller.
        /// </summary>
        /// <param name="trade">The trade entity with updated values</param>
        public void Update(Trade trade)
        {
            loanDbContext.Trades.Update(trade);
            // SaveChanges intentionally omitted to allow batching or deferred commits
        }
    }
}
