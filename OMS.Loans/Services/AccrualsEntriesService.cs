using DevExpress.Mvvm.UI.Native.ViewGenerator;
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
    public class AccrualsEntriesService : IAccrualEntries
    {
        private readonly LoanDbContext loanDbContext;

        public AccrualsEntriesService(LoanDbContext loanDbContext)
        {
            if (loanDbContext is null)
            {
                throw new ArgumentNullException(nameof(loanDbContext));
            }

            this.loanDbContext = loanDbContext;
        }
        public Accrual GetAccrual(int accrualId)
        {
           return loanDbContext.Accruals.AsNoTracking().FirstOrDefault(a => a.AccrualId == accrualId);
        }

        public IEnumerable<Accrual> GetAccruals(int tradeId)
        {
            var result= from a in loanDbContext.Accruals.AsNoTracking()
                   where a.TradeId == tradeId
                   select a;

            return result.ToList();
        }

        public int SaveAccrual(Accrual accrual)
        {
            loanDbContext.Add(accrual);
            loanDbContext.SaveChanges();
            return accrual.AccrualId;
        }

        public void Update(Accrual accrual)
        {
            loanDbContext.Update(accrual);
            loanDbContext.SaveChanges();
        }
    }
}
