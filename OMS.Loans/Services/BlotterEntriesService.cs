using LoanDbModel;
using Microsoft.EntityFrameworkCore;
using OMS.Loans.Common;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OMS.Loans.Services
{
    public class BlotterEntriesService : IBlotterEntries
    {
        private readonly LoanDbContext loanDbContext;

        public BlotterEntriesService(LoanDbContext loanDbContext)
        {
            this.loanDbContext = loanDbContext ?? throw new ArgumentNullException(nameof(loanDbContext));
        }
        public IEnumerable<Blotter> GetBlotterEntries()
        {
            return loanDbContext.BlotteredTrades.Include(i => i.CounterParty).ToList();
        }
    }
}
