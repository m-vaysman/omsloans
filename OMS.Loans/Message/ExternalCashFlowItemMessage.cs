using OMS.Loans.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OMS.Loans.Message
{
    public class ExternalCashFlowItemMessage
    {
        public ExternalCashFlowItemMessage(ExternalCashFlowItemViewModel value)
        {
            Value = value;
        }

        public ExternalCashFlowItemViewModel Value { get; }
    }

    public class RejectedExternalCashFlowItemMessage:ExternalCashFlowItemMessage
    {
        public RejectedExternalCashFlowItemMessage(ExternalCashFlowItemViewModel value):base(value)
        {
            
        }
    }
}
