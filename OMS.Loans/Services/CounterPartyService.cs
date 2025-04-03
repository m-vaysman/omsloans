using LoanDbModel;
using OMS.Loans.Common;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OMS.Loans.Services
{
    public class CounterPartyService : ICounterParties
    {
        private readonly LoanDbContext loanDbContext;

        public CounterPartyService(LoanDbContext loanDbContext)
        {
            if (loanDbContext is null)
            {
                throw new ArgumentNullException(nameof(loanDbContext));
            }

            this.loanDbContext = loanDbContext;
        }
        public IEnumerable<CounterParty> GetCounterParties()
        {
            return loanDbContext.CounterParties.ToList();
        }
    }
}
