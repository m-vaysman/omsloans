using OMS.Loans.Common.DTO;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OMS.Loans.Common
{
    public interface ITradeBalanceVsAccrued
    {
        IEnumerable<DailyBalanceVsAccrued> GetDailyBalanceVsAccrued(int tradeId);
    }
}
