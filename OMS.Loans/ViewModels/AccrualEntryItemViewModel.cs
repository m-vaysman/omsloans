using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.ComponentModel.DataAnnotations;

namespace OMS.Loans.ViewModels
{
    /// <summary>
    /// ViewModel representing a single accrual entry in the UI.
    /// Implements property change notifications, validation, and derived value calculation.
    /// </summary>
    public partial class AccrualEntryItemViewModel : ObservableValidator
    {
        // Primary key (for existing entries)
        [ObservableProperty]
        public int accrualId;

        // Link to a parent accrual if this is a derived entry
        [ObservableProperty]
        public int? parentAccrualId;

        // Read-only display code for UI display or tracking
        public string AccrualCode { get; }

        // Trade ID this accrual belongs to
        [ObservableProperty]
        public int tradeId;

        // Notional amount must be greater than 0
        [ObservableProperty]
        [NotifyDataErrorInfo]
        [Range(0.01, double.MaxValue, ErrorMessage = "Notional must be greater than zero.")]
        public decimal notional;

        // Accrual start date (required)
        [ObservableProperty]
        [NotifyDataErrorInfo]
        [Required(ErrorMessage = "From Date is required.")]
        public DateOnly fromDate;

        // Accrual end date (required)
        [ObservableProperty]
        [NotifyDataErrorInfo]
        [Required(ErrorMessage = "Start Date is required.")]
        public DateOnly toDate;

        // Interest rate from bank (percentage 0–100)
        [ObservableProperty]
        [NotifyDataErrorInfo]
        [Range(0, 100, ErrorMessage = "Bank Rate must be between 0 and 100.")]
        public decimal bankRate;

        // Spread over the bank rate (percentage 0–100)
        [ObservableProperty]
        [NotifyDataErrorInfo]
        [Range(0, 100, ErrorMessage = "Spread must be between 0 and 100.")]
        public decimal spread;

        // Day count convention (e.g., "360" or "365") — required, max 50 chars
        [ObservableProperty]
        [NotifyDataErrorInfo]
        [Required(ErrorMessage = "Act is required.")]
        [StringLength(50, ErrorMessage = "Act cannot exceed 50 characters.")]
        public string act;

        // Optional note field for comments, max 500 chars
        [ObservableProperty]
        [NotifyDataErrorInfo]
        [StringLength(500, ErrorMessage = "Note cannot exceed 500 characters.")]
        public string note;

        // Derived expected cash value, recalculated when key inputs change
        [ObservableProperty]
        private decimal expectedCash;

        #region Auto-recalculate expected cash on property change

        partial void OnNotionalChanged(decimal value) => RecalculateExpectedAccrualCash();
        partial void OnToDateChanged(DateOnly value) => RecalculateExpectedAccrualCash();
        partial void OnFromDateChanged(DateOnly value) => RecalculateExpectedAccrualCash();
        partial void OnBankRateChanged(decimal value) => RecalculateExpectedAccrualCash();
        partial void OnSpreadChanged(decimal value) => RecalculateExpectedAccrualCash();
        partial void OnActChanged(string value) => RecalculateExpectedAccrualCash();

        #endregion

        /// <summary>
        /// Constructor for initializing with an accrual code (read-only display).
        /// </summary>
        public AccrualEntryItemViewModel(string accrualCode)
        {
            AccrualCode = accrualCode;
        }

        /// <summary>
        /// Parameterless constructor required for AutoMapper or deserialization.
        /// </summary>
        public AccrualEntryItemViewModel() { }

        /// <summary>
        /// Calculates expected accrual cash using the formula:
        /// Notional * Days * (BankRate + Spread) / DayCountBasis
        /// </summary>
        public void RecalculateExpectedAccrualCash()
        {
            // Convert DateOnly to DateTime for arithmetic
            int days = (ToDate.ToDateTime(TimeOnly.MinValue) - FromDate.ToDateTime(TimeOnly.MinValue)).Days;

            var partialCalc = Notional * days * (BankRate + Spread);

            // Use 360 or 365 based on "Act" value
            ExpectedCash = Act == "360"
                ? partialCalc / 360
                : partialCalc / 365;
        }
    }
}
