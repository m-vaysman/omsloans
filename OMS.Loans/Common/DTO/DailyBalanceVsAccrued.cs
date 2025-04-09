using DevExpress.Xpf.Editors.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OMS.Loans.Common.DTO
{
    public class DailyBalanceVsAccrued
    {
        public DailyBalanceVsAccrued(int tradeId,decimal tradeBalance,decimal accruedBalance,DateOnly tradeDate, DateOnly settlementDate)
        {
            this.TradeId = tradeId;
            this.TradeBalance = tradeBalance;
            this.AccruedBalance = accruedBalance;
            this.TradeDate = tradeDate;
            this.SettlementDate = settlementDate;

        }

        public int TradeId { get; }
        public decimal TradeBalance { get; }
        public decimal AccruedBalance { get; }
        public DateOnly TradeDate { get; }
        public DateOnly SettlementDate { get; }
        public bool HasBreak => TradeBalance != AccruedBalance;
    }
}
