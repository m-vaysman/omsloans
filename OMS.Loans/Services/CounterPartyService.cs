using LoanDbModel;
using OMS.Loans.Common;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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
            CounterParties = new();
        }

        public ObservableCollection<CounterParty> CounterParties { get; }

        public IEnumerable<CounterParty> GetCounterParties()
        {
            CounterParties.Clear();
             loanDbContext.CounterParties.ToList().ForEach(c=>CounterParties.Add(c));
            return CounterParties;
        }
    }
}
