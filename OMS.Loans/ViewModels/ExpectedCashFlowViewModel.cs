using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using OMS.Loans.Infrastructure;
using OMS.Loans.Message;
using System;
using System.Collections.ObjectModel;

namespace OMS.Loans.ViewModels
{
    public partial class ExpectedCashFlowViewModel : ObservableValidator
    {
        [ObservableProperty]
        private ObservableCollection<ExpectedCashFlowItemViewModel> expectedCashFlowItems = new();

        [ObservableProperty]
        private ExpectedCashFlowItemViewModel selectedItem;

        public ExpectedCashFlowViewModel()
        {
            WeakReferenceMessenger.Default.Register<RejectedExpectedCashFlowItemMessage>(this, RejectedExpectedCashFlowItemMessage_Handler);
        }

        [RelayCommand]
        public void PublishSelectedItem()
        {

            WeakReferenceMessenger.Default.Send(new ExpectedCashFlowItemMessage(SelectedItem));

            expectedCashFlowItems.RemoveByReference(selectedItem);
        }

        public void RejectedExpectedCashFlowItemMessage_Handler(object sender, RejectedExpectedCashFlowItemMessage message)
        {
            ExpectedCashFlowItems.Add(message.Value);
        }

    }

    /// <summary>
    /// ViewModel representing a matchable cash item, derived from either an expected or actual cash record.
    /// Supports unified handling of external vs internal sources in the UI.
    /// </summary>
    public partial class CashMatchItemViewModel : ObservableObject
    {
        /// <summary>
        /// The original ViewModel (Expected or External) from which this item was constructed.
        /// Useful for identification, removal, or further processing.
        /// </summary>
        public object OriginalVm { get; }

        /// <summary>
        /// Indicates whether the source is 'expectedCash' or 'externalCash'.
        /// </summary>
        [ObservableProperty]
        private string sourceType;

        /// <summary>
        /// Date of the cash event (expected or actual).
        /// </summary>
        [ObservableProperty]
        private DateOnly date;

        /// <summary>
        /// Description of the payment source ("Payment", "Forecast", etc.).
        /// </summary>
        [ObservableProperty]
        private string source;

        /// <summary>
        /// Counterparty involved in the transaction.
        /// </summary>
        [ObservableProperty]
        private string counterParty;

        /// <summary>
        /// Business identifier for the transaction, such as a code or reference number.
        /// </summary>
        [ObservableProperty]
        private string code;

        /// <summary>
        /// Amount of the transaction. Expected cash is positive, external cash is negative.
        /// </summary>
        [ObservableProperty]
        private decimal amount;

        /// <summary>
        /// Initializes a new match item from an ExpectedCashFlowItemViewModel.
        /// </summary>
        /// <param name="expectedCash">The expected cash item to wrap.</param>
        public CashMatchItemViewModel(ExpectedCashFlowItemViewModel expectedCash)
        {
            if (expectedCash is null)
                throw new ArgumentNullException(nameof(expectedCash));

            SourceType = "expectedCash";
            Date = expectedCash.ExpectedCashPaymentDate;
            Source = expectedCash.Source;
            CounterParty = expectedCash.CounterParty;
            Code = expectedCash.Code;
            Amount = expectedCash.Amount;
            OriginalVm = expectedCash;
        }

        /// <summary>
        /// Initializes a new match item from an ExternalCashFlowItemViewModel.
        /// </summary>
        /// <param name="externalCash">The external cash item to wrap.</param>
        public CashMatchItemViewModel(ExternalCashFlowItemViewModel externalCash)
        {
            if (externalCash is null)
                throw new ArgumentNullException(nameof(externalCash));

            SourceType = "externalCash";
            Date = externalCash.Date;
            Source = "Payment";
            Code = string.IsNullOrWhiteSpace(externalCash.SubCode)
                ? externalCash.Code
                : externalCash.SubCode;
            CounterParty = externalCash.CounterParty;
            Amount = -1.0m * externalCash.Amount; // External cash treated as negative
            OriginalVm = externalCash;
        }
    }
}
