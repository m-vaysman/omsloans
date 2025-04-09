using LoanDbModel;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OMS.Loans.Common
{
    public interface ITradeDocumentRetriever
    {
        IEnumerable<TradeDocument> GetTradeDocuments(int tradeId);
    }
}
