using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OMS.Loans.ViewModels
{
    class CashMatchingViewModel
    {
        public ObservableCollection<decimal> LeftItems { get; } = new() { 100, 200, 300 };
        public ObservableCollection<decimal> RightItems { get; } = new() { 400, 500, 600 };
        public ObservableCollection<PairItem> MiddleItems { get; } = new();
    }

    public class PairItem
    {
        public decimal? Left { get; set; }
        public decimal? Right { get; set; }

        public string Display => $"${Left} + ${Right}";
    }
}
