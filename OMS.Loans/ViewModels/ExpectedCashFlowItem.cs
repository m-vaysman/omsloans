using CommunityToolkit.Mvvm.ComponentModel;
using System;

namespace OMS.Loans.ViewModels
{
    /// <summary>
    /// ViewModel representing an expected cash flow item.
    /// Includes validation support via ObservableValidator.
    /// </summary>
    public partial class ExpectedCashFlowItemViewModel : ObservableValidator
    {
        /// <summary>
        /// Unique code identifying the expected cash flow item.
        /// </summary>
        [ObservableProperty]
        private string code;

        /// <summary>
        /// Name of the counterparty associated with this cash flow.
        /// </summary>
        [ObservableProperty]
        private string counterParty;

        /// <summary>
        /// Source or description of the expected payment (e.g., 'Loan Interest').
        /// </summary>
        [ObservableProperty]
        private string source;

        /// <summary>
        /// Expected cash amount (positive).
        /// </summary>
        [ObservableProperty]
        private decimal amount;

        /// <summary>
        /// Date when the payment is expected to be received.
        /// </summary>
        [ObservableProperty]
        private DateOnly expectedCashPaymentDate;
    }
}
