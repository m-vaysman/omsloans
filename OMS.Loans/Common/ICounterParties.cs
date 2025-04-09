using LoanDbModel;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace OMS.Loans.Common
{
    public interface ICounterParties
    {
        public IEnumerable<CounterParty> GetCounterParties();
        public ObservableCollection<CounterParty> CounterParties { get; }
    }
}
