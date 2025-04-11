using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using OMS.Loans.Infrastructure;
using OMS.Loans.Message;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OMS.Loans.ViewModels
{
    public partial class ExpectedCashFlowViewModel:ObservableValidator
    {
        [ObservableProperty]
        private ObservableCollection<ExpectedCashFlowItemViewModel> expectedCashFlowItems=new();

        [ObservableProperty]
        private ExpectedCashFlowItemViewModel selectedItem;

        public ExpectedCashFlowViewModel()
        {
            WeakReferenceMessenger.Default.Register<RejectedExpectedCashFlowItemMessage>(this, RejectedExpectedCashFlowItemMessage_Handler);
        }

        [RelayCommand]
        public void PublishSelectedItem() {

                WeakReferenceMessenger.Default.Send(new ExpectedCashFlowItemMessage(SelectedItem));
              
                expectedCashFlowItems.RemoveByReference(selectedItem);
        }

        public void RejectedExpectedCashFlowItemMessage_Handler(object sender, RejectedExpectedCashFlowItemMessage message)
        {
            ExpectedCashFlowItems.Add(message.Value);
        }

    }
}
