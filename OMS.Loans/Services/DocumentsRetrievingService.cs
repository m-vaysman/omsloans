using LoanDbModel;
using Microsoft.EntityFrameworkCore;
using OMS.Loans.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

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
                                .AsNoTracking()
                                .Where(t => t.TradeId == tradeId)
                                .Select(t => new TradeDocument
                                {
                                    TradeDocumentId = t.TradeDocumentId,
                                    TradeId = t.TradeId,
                                    FileName = t.FileName,
                                    ContentType = t.ContentType
                                })
                                .ToList();
        }

        public async Task<byte[]> GetDocumentData(int tradeDocumentId)
        {
            var data = await loanDbContext.TradeDocuments
                                .AsNoTracking()
                                .Where(t => t.TradeDocumentId == tradeDocumentId)
                                .Select(t => t.Data)
                                .FirstOrDefaultAsync();

            if (data == null)
            {
                throw new InvalidOperationException($"TradeDocument {tradeDocumentId} not found.");
            }

            return data;
        }
    }
}
