using LoanDbModel;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace OMS.Loans.Common
{
    public interface IBlotterEntries
    {
        Task<IReadOnlyList<Blotter>> GetBlotterEntries();
    }
}
