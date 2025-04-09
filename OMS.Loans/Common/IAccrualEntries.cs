using LoanDbModel;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OMS.Loans.Common
{
    public interface IAccrualEntries
    {
        void Update(Accrual accrual);
        Accrual GetAccrual(int accrualId);
        int SaveAccrual(Accrual accrual);
        IEnumerable<Accrual> GetAccruals(int tradeId);
    }
}
