using LoanDbModel;
using System.Collections.Generic;

namespace OMS.Loans.Common
{
    public interface IBlotterEntries
    {
        IEnumerable<Blotter> GetBlotterEntries();
    }
}
