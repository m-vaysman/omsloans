using LoanDbModel;
using Microsoft.EntityFrameworkCore;
using OMS.Loans.Common;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OMS.Loans.Services
{
    /// <summary>
    /// Service for accessing blotter entries from the database.
    /// Implements the IBlotterEntries interface.
    /// </summary>
    public class BlotterEntriesService : IBlotterEntries
    {
        private readonly LoanDbContext loanDbContext;

        /// <summary>
        /// Constructor with dependency injection of the LoanDbContext.
        /// </summary>
        /// <param name="loanDbContext">Entity Framework DbContext for loan data</param>
        /// <exception cref="ArgumentNullException">Thrown if context is null</exception>
        public BlotterEntriesService(LoanDbContext loanDbContext)
        {
            this.loanDbContext = loanDbContext ?? throw new ArgumentNullException(nameof(loanDbContext));
        }

        /// <summary>
        /// Retrieves all blotter entries, including associated CounterParty data.
        /// </summary>
        /// <returns>List of blotter entries with eager-loaded counterparty info</returns>
        public IEnumerable<Blotter> GetBlotterEntries()
        {
            return loanDbContext.BlotteredTrades
                                .Include(i => i.CounterParty) // Eager load the related CounterParty
                                .ToList();                    // Materialize the query
        }
    }
}
