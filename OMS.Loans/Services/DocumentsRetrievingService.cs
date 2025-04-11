using LoanDbModel;
using Microsoft.EntityFrameworkCore;
using OMS.Loans.Common;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OMS.Loans.Services
{
    /// <summary>
    /// Service responsible for retrieving trade-related documents from the database.
    /// Implements the ITradeDocumentRetriever interface.
    /// </summary>
    public class DocumentsRetrievingService : ITradeDocumentRetriever
    {
        private readonly LoanDbContext loanDbContext;

        /// <summary>
        /// Constructor that initializes the service with the given EF Core DbContext.
        /// </summary>
        /// <param name="loanDbContext">Database context for accessing loan and trade data</param>
        /// <exception cref="ArgumentNullException">Thrown if the context is null</exception>
        public DocumentsRetrievingService(LoanDbContext loanDbContext)
        {
            this.loanDbContext = loanDbContext ?? throw new ArgumentNullException(nameof(loanDbContext));
        }

        /// <summary>
        /// Retrieves all documents associated with the specified trade ID.
        /// </summary>
        /// <param name="tradeId">The unique identifier of the trade</param>
        /// <returns>A list of trade documents</returns>
        public IEnumerable<TradeDocument> GetTradeDocuments(int tradeId)
        {
            return loanDbContext.TradeDocuments
                                .AsNoTracking()                  // Optimize for read-only operation
                                .Where(t => t.TradeId == tradeId) // Filter documents by trade ID
                                .ToList();                        // Execute the query
        }
    }
}
