using AutoMapper.Internal.Mappers;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using OMS.Loans.Infrastructure;
using OMS.Loans.Message;
using System.Collections.ObjectModel;
using System.Linq;

namespace OMS.Loans.ViewModels
{
    public partial class ExpectedCashFlowItemMergedViewModel:ObservableObject
    {
        [ObservableProperty]
        private ObservableCollection<CashMatchItemViewModel> cashMatchingItems=new();
        [ObservableProperty]
        private ObservableCollection<ExpectedCashFlowItemViewModel> expectedCash = new();
        [ObservableProperty]
        private ObservableCollection<ExternalCashFlowItemViewModel> cashReceipts = new();

        [ObservableProperty]
        private decimal expectedCashTotal;
        [ObservableProperty]
        private decimal cashReceiptsTotal;

        [ObservableProperty]
        private bool collectionIsNotEmpty;

        [ObservableProperty]
        private CashMatchItemViewModel selectedItem;

        public ExpectedCashFlowItemMergedViewModel()
        {
            expectedCash.CollectionChanged += ExpectedCash_CollectionChanged;
            cashReceipts.CollectionChanged += CashReceipts_CollectionChanged;
            cashMatchingItems.CollectionChanged += CashMatchingItems_CollectionChanged;
            WeakReferenceMessenger.Default.Register<ExpectedCashFlowItemMessage>(this, ExpectedCashFlowItemMessage_Handler);
            WeakReferenceMessenger.Default.Register<ExternalCashFlowItemMessage>(this, ExternalCashFlowItemMessage_Handler);
            this.CollectionIsNotEmpty = false;
            ApplyCashCommand.NotifyCanExecuteChanged();
        }

        private void CashMatchingItems_CollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            CollectionIsNotEmpty = cashMatchingItems.Count > 0;
            ApplyCashCommand.NotifyCanExecuteChanged();
        }

        private void ExternalCashFlowItemMessage_Handler(object recipient, ExternalCashFlowItemMessage message)
        {
            if (message.Value != null)
            {
                var cashItem = new CashMatchItemViewModel(message.Value);
                CashMatchingItems.Add(cashItem);

                CashReceipts.Add(message.Value);
            }
        }

        private void ExpectedCashFlowItemMessage_Handler(object sender, ExpectedCashFlowItemMessage message) {
            if (message.Value != null)
            {

                var cashItem = new CashMatchItemViewModel(message.Value);
                CashMatchingItems.Add(cashItem);

                ExpectedCash.Add(message.Value);
            }
        }
        private void CashReceipts_CollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            var ee = "d";
        }

        private void ExpectedCash_CollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            var ee = e;
        }


        public bool CanApplyCash() {
            if (cashMatchingItems.Count == 0)
                return false;

          return  cashMatchingItems.Sum(i => i.Amount) == 0;
        }

        [RelayCommand(CanExecute =nameof(CanApplyCash))]
        public void ApplyCash() {
        
        
        }

        [RelayCommand]
        public void RemoveSelectedItem()
        {
            if (SelectedItem.OriginalVm.GetType() == typeof(ExternalCashFlowItemViewModel)) {
                WeakReferenceMessenger.Default.Send<RejectedExternalCashFlowItemMessage>(new (SelectedItem.OriginalVm as ExternalCashFlowItemViewModel));
                CashMatchingItems.RemoveByReference(SelectedItem);
                return;
            }

            if (SelectedItem.OriginalVm.GetType() == typeof(ExpectedCashFlowItemViewModel))
            {
                WeakReferenceMessenger.Default.Send<RejectedExpectedCashFlowItemMessage>(new(SelectedItem.OriginalVm as ExpectedCashFlowItemViewModel));
                CashMatchingItems.RemoveByReference(SelectedItem);
                return;
            }

        }




    }
}
