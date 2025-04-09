using DevExpress.Xpf.Editors.Helpers;
using LoanDbModel;
using Microsoft.EntityFrameworkCore;
using OMS.Loans.Common;
using OMS.Loans.Common.DTO;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Threading.Tasks;

namespace OMS.Loans.Services
{
    public class TradeBalanceVsAccruedService : ITradeBalanceVsAccrued
    {
        private readonly LoanDbContext loanDbContext;

        public TradeBalanceVsAccruedService(LoanDbContext loanDbContext)
        {
            this.loanDbContext = loanDbContext ?? throw new ArgumentNullException(nameof(loanDbContext));
        }
        public IEnumerable<DailyBalanceVsAccrued> GetDailyBalanceVsAccrued(int tradeId)
        {
          var trade=  loanDbContext.Trades.First(t => t.TradeId == tradeId);
            var accruals = loanDbContext.Accruals.Where(t => t.TradeId == tradeId);

            var tradeDate = new DateOnly(trade.TradeDate.Year, trade.TradeDate.Month, trade.TradeDate.Day);
            var tradeSettlementDateTime = trade.SettlementDate;
            var tradeSettlementDate = new DateOnly(tradeSettlementDateTime.Year, tradeSettlementDateTime.Month, tradeSettlementDateTime.Day);
            var lastAccrualDateTime = accruals.Max(a => a.ToDate);
            var lastAccrualDate = new DateOnly(lastAccrualDateTime.Year, lastAccrualDateTime.Month, lastAccrualDateTime.Day);
            var nextDate = tradeSettlementDate;

            var accrualBalance = from a in accruals
                                 group a by a.FromDate into g
                                 select new
                                 {
                                     Date = g.Key,
                                     Balance = g.Sum(i => i.Notional)
                                 };
            var balances = accrualBalance.OrderBy(i=>i.Date).ToList();

            while (nextDate <= lastAccrualDate)
            {
                var dailyAccrualBalance = balances.Where(i => i.Date <= nextDate).First();
                yield return new DailyBalanceVsAccrued(trade.TradeId, trade.Notional, dailyAccrualBalance.Balance, tradeDate, tradeSettlementDate);
                nextDate = nextDate.AddDays(1);
            }
        }
    }
}
