using LoanDbModel;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OMS.Loans.Common
{
    public interface ITradeEntries
    {
        public int Save(Trade trade);
        public Trade Get(int id);

        public void Update(Trade trade);

    }
}
