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
    /// <summary>
    /// ViewModel responsible for managing and merging expected cash flow and external cash receipt items.
    /// Combines both into a single matching collection and supports commands for applying or removing matches.
    /// </summary>
    public partial class ExpectedCashFlowItemMergedViewModel : ObservableObject
    {
        // Collection of merged cash match items (combined from expected and received flows)
        [ObservableProperty]
        private ObservableCollection<CashMatchItemViewModel> cashMatchingItems = new();

        // Collection of expected (outgoing) cash flow items
        [ObservableProperty]
        private ObservableCollection<ExpectedCashFlowItemViewModel> expectedCash = new();

        // Collection of external (incoming) cash receipts
        [ObservableProperty]
        private ObservableCollection<ExternalCashFlowItemViewModel> cashReceipts = new();

        // Total expected cash (outgoing)
        [ObservableProperty]
        private decimal expectedCashTotal;

        // Total incoming cash (receipts)
        [ObservableProperty]
        private decimal cashReceiptsTotal;

        // Flag indicating if any items are in the matching collection
        [ObservableProperty]
        private bool collectionIsNotEmpty;

        // The currently selected item in the UI (from the match list)
        [ObservableProperty]
        private CashMatchItemViewModel selectedItem;

        /// <summary>
        /// Constructor initializes collections, wires up change events and message subscriptions.
        /// </summary>
        public ExpectedCashFlowItemMergedViewModel()
        {
            expectedCash.CollectionChanged += ExpectedCash_CollectionChanged;
            cashReceipts.CollectionChanged += CashReceipts_CollectionChanged;
            cashMatchingItems.CollectionChanged += CashMatchingItems_CollectionChanged;

            WeakReferenceMessenger.Default.Register<ExpectedCashFlowItemMessage>(this, ExpectedCashFlowItemMessage_Handler);
            WeakReferenceMessenger.Default.Register<ExternalCashFlowItemMessage>(this, ExternalCashFlowItemMessage_Handler);

            this.CollectionIsNotEmpty = false;
            ApplyCashCommand.NotifyCanExecuteChanged(); // Prime the command availability
        }

        /// <summary>
        /// Updates `CollectionIsNotEmpty` and notifies the Apply command availability.
        /// </summary>
        private void CashMatchingItems_CollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            CollectionIsNotEmpty = cashMatchingItems.Count > 0;
            ApplyCashCommand.NotifyCanExecuteChanged();
        }

        /// <summary>
        /// Handles receipt of a new external cash item via messaging.
        /// Adds it to both receipts and matching collections.
        /// </summary>
        private void ExternalCashFlowItemMessage_Handler(object recipient, ExternalCashFlowItemMessage message)
        {
            if (message.Value != null)
            {
                var cashItem = new CashMatchItemViewModel(message.Value);
                CashMatchingItems.Add(cashItem);
                CashReceipts.Add(message.Value);
            }
        }

        /// <summary>
        /// Handles receipt of a new expected cash item via messaging.
        /// Adds it to both expected and matching collections.
        /// </summary>
        private void ExpectedCashFlowItemMessage_Handler(object sender, ExpectedCashFlowItemMessage message)
        {
            if (message.Value != null)
            {
                var cashItem = new CashMatchItemViewModel(message.Value);
                CashMatchingItems.Add(cashItem);
                ExpectedCash.Add(message.Value);
            }
        }

        /// <summary>
        /// Handler stub for external cash receipt changes (not currently used).
        /// </summary>
        private void CashReceipts_CollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            // Placeholder - implement logic if needed
        }

        /// <summary>
        /// Handler stub for expected cash flow changes (not currently used).
        /// </summary>
        private void ExpectedCash_CollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            // Placeholder - implement logic if needed
        }

        /// <summary>
        /// Determines if the ApplyCash command is allowed:
        /// Must have items, and their net total must be zero.
        /// </summary>
        public bool CanApplyCash()
        {
            if (cashMatchingItems.Count == 0)
                return false;

            return cashMatchingItems.Sum(i => i.Amount) == 0;
        }

        /// <summary>
        /// Command for applying cash matches. Only enabled when `CanApplyCash` is true.
        /// </summary>
        [RelayCommand(CanExecute = nameof(CanApplyCash))]
        public void ApplyCash()
        {
            // Matching logic can be implemented here.
        }

        /// <summary>
        /// Command to remove the currently selected item from the matching list.
        /// Also sends rejection messages based on original type.
        /// </summary>
        [RelayCommand]
        public void RemoveSelectedItem()
        {
            if (SelectedItem.OriginalVm is ExternalCashFlowItemViewModel externalVm)
            {
                WeakReferenceMessenger.Default.Send(new RejectedExternalCashFlowItemMessage(externalVm));
                CashMatchingItems.RemoveByReference(SelectedItem);
                return;
            }

            if (SelectedItem.OriginalVm is ExpectedCashFlowItemViewModel expectedVm)
            {
                WeakReferenceMessenger.Default.Send(new RejectedExpectedCashFlowItemMessage(expectedVm));
                CashMatchingItems.RemoveByReference(SelectedItem);
                return;
            }
        }
    }
}
