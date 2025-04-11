using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OMS.Loans.ViewModels
{
    public partial class ExpectedCashFlowItemViewModel:ObservableValidator
    {
        [ObservableProperty]
        private string code;
        [ObservableProperty]
        private string counterParty;
        [ObservableProperty]
        private string source;
        [ObservableProperty]
        private decimal amount;
        [ObservableProperty]
        private DateOnly expectedCashPaymentDate;

    }
    public partial class CashMatchItemViewModel : ObservableObject {

        public object OriginalVm {get;}

        [ObservableProperty]
        string sourceType;
        [ObservableProperty]
        DateOnly date;
        [ObservableProperty]
        string source;
        [ObservableProperty]
        string counterParty;
        [ObservableProperty]
        string code;
        [ObservableProperty]
        decimal amount;

        public CashMatchItemViewModel(ExpectedCashFlowItemViewModel expectedCash)
        {
            if (expectedCash is null)
            {
                throw new ArgumentNullException(nameof(expectedCash));
            }

            SourceType = "expectedCash";
            Date = expectedCash.ExpectedCashPaymentDate;
            Source = expectedCash.Source;
            CounterParty = expectedCash.CounterParty;
            Code = expectedCash.Code;
            Amount = expectedCash.Amount;
            OriginalVm = expectedCash;
            
        }

        public CashMatchItemViewModel(ExternalCashFlowItemViewModel externalCash)
        {
            if (externalCash is null)
            {
                throw new ArgumentNullException(nameof(externalCash));
            }

            SourceType = "externalCash";
            Date = externalCash.Date;
            Source = "Payment";
            Code = string.IsNullOrWhiteSpace(externalCash.SubCode)?externalCash.Code:externalCash.SubCode;
            counterParty = externalCash.CounterParty;
            Amount = -1.0m*externalCash.Amount;
            OriginalVm = externalCash;
        }



    }
}
