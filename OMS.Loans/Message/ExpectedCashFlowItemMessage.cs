using OMS.Loans.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OMS.Loans.Message
{
    public class ExpectedCashFlowItemMessage
    {

        public ExpectedCashFlowItemMessage(ExpectedCashFlowItemViewModel value)
        {
            Value = value;
        }

        public ExpectedCashFlowItemViewModel Value { get; }
    }

    public class RejectedExpectedCashFlowItemMessage: ExpectedCashFlowItemMessage
    {
        public RejectedExpectedCashFlowItemMessage(ExpectedCashFlowItemViewModel value):base(value)
        {
            
        }
    }
}
