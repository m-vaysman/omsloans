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
        /// Updates an existing trade and saves it.
        /// A trade booked earlier in this context is already tracked; its values are copied
        /// onto that instance so a second booking does not attach a duplicate key.
        /// New documents on the incoming trade are inserted. Navigations are not replaced.
        /// </summary>
        /// <param name="trade">The trade entity with updated values</param>
        public void Update(Trade trade)
        {
            if (trade is null)
            {
                throw new ArgumentNullException(nameof(trade));
            }

            var newDocuments = trade.TradeDocuments?
                .Where(document => document.TradeDocumentId == 0)
                .ToList();

            trade.TradeDocuments = null;
            trade.CounterParty = null;
            trade.Paydowns = null;
            trade.Accruals = null;

            var tracked = loanDbContext.Trades.Local
                .FirstOrDefault(existing => existing.TradeId == trade.TradeId);

            if (tracked != null)
            {
                loanDbContext.Entry(tracked).CurrentValues.SetValues(trade);
            }
            else
            {
                loanDbContext.Trades.Update(trade);
            }

            if (newDocuments != null)
            {
                foreach (var document in newDocuments)
                {
                    document.Trade = null;
                    document.TradeId = trade.TradeId;
                    loanDbContext.TradeDocuments.Add(document);
                }
            }

            loanDbContext.SaveChanges();
        }
    }
}
