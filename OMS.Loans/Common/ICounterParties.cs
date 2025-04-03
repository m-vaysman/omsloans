using LoanDbModel;
using System.Collections.Generic;

namespace OMS.Loans.Common
{
    public interface ICounterParties
    {
        public IEnumerable<CounterParty> GetCounterParties();
    }
}
