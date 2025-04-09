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
    public class TradeEntriesService : ITradeEntries
    {
        private readonly LoanDbContext loanDbContext;

        public TradeEntriesService(LoanDbContext loanDbContext)
        {
            
            if (loanDbContext is null)
            {
                throw new ArgumentNullException(nameof(loanDbContext));
            }

            this.loanDbContext = loanDbContext;
        }

        public Trade Get(int id)
        {
          return loanDbContext.Trades.AsNoTracking().FirstOrDefault(t => t.TradeId == id);
        }

        public int Save(Trade trade)
        {
            loanDbContext.Trades.Add(trade);
            loanDbContext.SaveChanges();
            return trade.TradeId;
        }

        public void Update(Trade trade)
        {
          loanDbContext.Trades.Update(trade);
        }

   
    }
}
