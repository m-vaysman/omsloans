using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OMS.Loans.ViewModels { 
    public class TradeEntryDesignViewModel:TradeEntryViewModel
    {
    public TradeEntryDesignViewModel()
    {
        this.TradeEntryItem = new TradeEntryItem()
        {
            CommitmentReduction = 50000
        };


    }
}
}
