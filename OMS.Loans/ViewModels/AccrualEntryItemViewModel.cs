using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel.DataAnnotations;

namespace OMS.Loans.ViewModels
{
    public partial class AccrualEntryItemViewModel : ObservableValidator
    {
        [ObservableProperty]
         public int accrualId;

        [ObservableProperty]
        public int? parentAccrualId;

        public string AccrualCode { get; }

        [ObservableProperty]
        public int tradeId;

        [ObservableProperty]
        [NotifyDataErrorInfo]
        [Range(0.01, double.MaxValue, ErrorMessage = "Notional must be greater than zero.")]
        public decimal notional;

        [ObservableProperty]
        [NotifyDataErrorInfo]
        [Required(ErrorMessage = "From Date is required.")]
        public DateOnly fromDate;

        [ObservableProperty]
        [NotifyDataErrorInfo]
        [Required(ErrorMessage = "Start Date is required.")]
        public DateOnly toDate;

        [ObservableProperty]
        [NotifyDataErrorInfo]
        [Range(0, 100, ErrorMessage = "Bank Rate must be between 0 and 100.")]
        public decimal bankRate;

        [ObservableProperty]
        [NotifyDataErrorInfo]
        [Range(0, 100, ErrorMessage = "Spread must be between 0 and 100.")]
        public decimal spread;

        [ObservableProperty]
        [NotifyDataErrorInfo]
        [Required(ErrorMessage = "Act is required.")]
        [StringLength(50, ErrorMessage = "Act cannot exceed 50 characters.")]
        public string act;

        [ObservableProperty]
        [NotifyDataErrorInfo]
        [StringLength(500, ErrorMessage = "Note cannot exceed 500 characters.")]
        public string note;

        [ObservableProperty]
        private decimal expectedCash;


        partial void OnNotionalChanged(decimal value) => RecalculateExpectedAccrualCash();
        partial void OnToDateChanged(DateOnly value) => RecalculateExpectedAccrualCash();
        partial void OnFromDateChanged(DateOnly value) => RecalculateExpectedAccrualCash();
        partial void OnBankRateChanged(decimal value) => RecalculateExpectedAccrualCash();
        partial void OnSpreadChanged(decimal value) => RecalculateExpectedAccrualCash();
        partial void OnActChanged(string value) => RecalculateExpectedAccrualCash();
        public AccrualEntryItemViewModel(string accrualCode)
        {
            AccrualCode = accrualCode;
        }

        // Parameterless constructor for AutoMapper support
        public AccrualEntryItemViewModel() { }

        public void RecalculateExpectedAccrualCash()
        {
            var partialCalc = Notional * (ToDate.ToDateTime(TimeOnly.MinValue) - FromDate.ToDateTime(TimeOnly.MinValue)).Days
                     * (BankRate + Spread);

            if (Act == "360")
                ExpectedCash = partialCalc / 360;
            else
                ExpectedCash = partialCalc / 365;
                    
        }
    }
}