using LoanDbModel;
using OMS.Loans.Common;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace OMS.Loans.Services
{
    /// <summary>
    /// Service responsible for retrieving and exposing counterparty records.
    /// Implements the ICounterParties interface.
    /// </summary>
    public class CounterPartyService : ICounterParties
    {
        private readonly LoanDbContext loanDbContext;

        /// <summary>
        /// Constructor that initializes the service with the EF Core DbContext.
        /// </summary>
        /// <param name="loanDbContext">Injected DbContext for loan-related data</param>
        /// <exception cref="ArgumentNullException">Thrown if context is null</exception>
        public CounterPartyService(LoanDbContext loanDbContext)
        {
            if (loanDbContext is null)
            {
                throw new ArgumentNullException(nameof(loanDbContext));
            }

            this.loanDbContext = loanDbContext;

            // Initialize the observable collection for data binding support
            CounterParties = new ObservableCollection<CounterParty>();
        }

        /// <summary>
        /// Collection of counterparties maintained in memory and exposed for binding.
        /// </summary>
        public ObservableCollection<CounterParty> CounterParties { get; }

        /// <summary>
        /// Loads counterparties from the database into the observable collection.
        /// </summary>
        /// <returns>A list of counterparties from the database</returns>
        public IEnumerable<CounterParty> GetCounterParties()
        {
            CounterParties.Clear();

            // Fetch all counterparties from the DB and populate the observable collection
            loanDbContext.CounterParties
                         .ToList()
                         .ForEach(c => CounterParties.Add(c));

            return CounterParties;
        }
    }
}
