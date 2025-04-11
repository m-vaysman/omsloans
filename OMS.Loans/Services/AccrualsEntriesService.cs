using DevExpress.Mvvm.UI.Native.ViewGenerator; // (Appears unused – may be removable)
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
    /// <summary>
    /// Service for accessing and modifying accrual entries in the loan database.
    /// Implements IAccrualEntries interface.
    /// </summary>
    public class AccrualsEntriesService : IAccrualEntries
    {
        private readonly LoanDbContext loanDbContext;

        /// <summary>
        /// Constructor ensures a valid database context is provided.
        /// </summary>
        /// <param name="loanDbContext">Injected EF Core DB context for loan data</param>
        /// <exception cref="ArgumentNullException"></exception>
        public AccrualsEntriesService(LoanDbContext loanDbContext)
        {
            if (loanDbContext is null)
                throw new ArgumentNullException(nameof(loanDbContext));

            this.loanDbContext = loanDbContext;
        }

        /// <summary>
        /// Retrieves a single accrual record by its ID.
        /// </summary>
        /// <param name="accrualId">The unique ID of the accrual</param>
        /// <returns>Accrual record or null if not found</returns>
        public Accrual GetAccrual(int accrualId)
        {
            return loanDbContext.Accruals
                                .AsNoTracking() // Read-only query for performance
                                .FirstOrDefault(a => a.AccrualId == accrualId);
        }

        /// <summary>
        /// Retrieves all accrual records associated with a specific trade.
        /// </summary>
        /// <param name="tradeId">The trade ID to search for</param>
        /// <returns>List of matching accruals</returns>
        public IEnumerable<Accrual> GetAccruals(int tradeId)
        {
            var result = from a in loanDbContext.Accruals.AsNoTracking()
                         where a.TradeId == tradeId
                         select a;

            return result.ToList();
        }

        /// <summary>
        /// Inserts a new accrual record into the database.
        /// </summary>
        /// <param name="accrual">The accrual entity to insert</param>
        /// <returns>The generated ID for the new accrual</returns>
        public int SaveAccrual(Accrual accrual)
        {
            loanDbContext.Add(accrual);
            loanDbContext.SaveChanges();
            return accrual.AccrualId;
        }

        /// <summary>
        /// Updates an existing accrual record.
        /// </summary>
        /// <param name="accrual">The accrual entity with updated values</param>
        public void Update(Accrual accrual)
        {
            loanDbContext.Update(accrual);
            loanDbContext.SaveChanges();
        }
    }
}
