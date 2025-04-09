using LoanDbModel;
using Microsoft.EntityFrameworkCore;
using OMS.Loans.Common;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OMS.Loans.Services
{
    public class DocumentsRetrievingService : ITradeDocumentRetriever
    {
        private readonly LoanDbContext loanDbContext;

        public DocumentsRetrievingService(LoanDbContext loanDbContext)
        {
            this.loanDbContext = loanDbContext ?? throw new ArgumentNullException(nameof(loanDbContext));
        }
        public IEnumerable<TradeDocument> GetTradeDocuments(int tradeId)
        {
          return  loanDbContext.TradeDocuments.AsNoTracking().Where(t => t.TradeId == tradeId).ToList();
        }
    }
}
