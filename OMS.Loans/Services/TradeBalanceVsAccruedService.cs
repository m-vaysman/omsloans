using DevExpress.Xpf.Editors.Helpers; // (Appears unused; consider removing if not needed)
using LoanDbModel;
using Microsoft.EntityFrameworkCore;
using OMS.Loans.Common;
using OMS.Loans.Common.DTO;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OMS.Loans.Services
{
    /// <summary>
    /// Service to compute the daily comparison of trade notional balance vs accrued notional balance.
    /// Implements the ITradeBalanceVsAccrued interface.
    /// </summary>
    public class TradeBalanceVsAccruedService : ITradeBalanceVsAccrued
    {
        private readonly LoanDbContext loanDbContext;

        /// <summary>
        /// Constructor with injected LoanDbContext.
        /// </summary>
        /// <param name="loanDbContext">EF Core DbContext for loan and trade data</param>
        /// <exception cref="ArgumentNullException">Thrown if the context is null</exception>
        public TradeBalanceVsAccruedService(LoanDbContext loanDbContext)
        {
            this.loanDbContext = loanDbContext ?? throw new ArgumentNullException(nameof(loanDbContext));
        }

        /// <summary>
        /// Retrieves a list of daily trade balance vs. accrued values from settlement until last accrual.
        /// </summary>
        /// <param name="tradeId">The trade ID to process</param>
        /// <returns>An enumerable of DailyBalanceVsAccrued DTOs</returns>
        public IEnumerable<DailyBalanceVsAccrued> GetDailyBalanceVsAccrued(int tradeId)
        {
            // Retrieve the target trade entity
            var trade = loanDbContext.Trades.First(t => t.TradeId == tradeId);

            // Fetch all accruals associated with the trade
            var accruals = loanDbContext.Accruals.Where(t => t.TradeId == tradeId);

            // Convert TradeDate and SettlementDate to DateOnly for easier date arithmetic
            var tradeDate = new DateOnly(trade.TradeDate.Year, trade.TradeDate.Month, trade.TradeDate.Day);
            var tradeSettlementDateTime = trade.SettlementDate;
            var tradeSettlementDate = new DateOnly(tradeSettlementDateTime.Year, tradeSettlementDateTime.Month, tradeSettlementDateTime.Day);

            // Determine the last accrual end date
            var lastAccrualDateTime = accruals.Max(a => a.ToDate);
            var lastAccrualDate = new DateOnly(lastAccrualDateTime.Year, lastAccrualDateTime.Month, lastAccrualDateTime.Day);

            // Start iteration from the settlement date
            var nextDate = tradeSettlementDate;

            // Aggregate accruals by their start date and sum notional amounts
            var accrualBalance = from a in accruals
                                 group a by a.FromDate into g
                                 select new
                                 {
                                     Date = g.Key,
                                     Balance = g.Sum(i => i.Notional)
                                 };

            // Sort balances by date
            var balances = accrualBalance.OrderBy(i => i.Date).ToList();

            // Yield daily comparison records from settlement date to last accrual date
            while (nextDate <= lastAccrualDate)
            {
                // Select the first balance where the FromDate is <= current date
                var dailyAccrualBalance = balances.First(i => i.Date <= nextDate);

                // Return result with trade and accrual context
                yield return new DailyBalanceVsAccrued(
                    trade.TradeId,
                    trade.Notional,
                    dailyAccrualBalance.Balance,
                    tradeDate,
                    tradeSettlementDate
                );

                // Advance to the next day
                nextDate = nextDate.AddDays(1);
            }
        }
    }
}
