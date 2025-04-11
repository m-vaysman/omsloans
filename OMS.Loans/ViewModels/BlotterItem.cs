using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;

namespace OMS.Loans.ViewModels
{
    /// <summary>
    /// ViewModel representing a blotter trade item.
    /// Includes property change notification and runtime validation.
    /// </summary>
    public partial class BlotterItem : ObservableObject, INotifyDataErrorInfo
    {
        // Blotter trade properties
        [ObservableProperty]
        public string counterPartyName;

        [ObservableProperty]
        public string ticker;

        [ObservableProperty]
        public int counterPartyId;

        [ObservableProperty]
        public string cusip;

        [ObservableProperty]
        public DateTime tradeDate;

        [ObservableProperty]
        public string buySell;

        [ObservableProperty]
        public decimal price;

        [ObservableProperty]
        public decimal globalCommitment;

        [ObservableProperty]
        public decimal notional;

        [ObservableProperty]
        public string tradeAcct;

        [ObservableProperty]
        public string document;

        [ObservableProperty]
        public string ticket;

        [ObservableProperty]
        public decimal spread;

        #region Validation Implementation (INotifyDataErrorInfo)

        // Dictionary holding validation errors per property
        private readonly Dictionary<string, List<string>> _errors = new();

        // True if any property has validation errors
        public bool HasErrors => _errors.Count > 0;

        // Event required by INotifyDataErrorInfo for UI to react to validation changes
        public event EventHandler<DataErrorsChangedEventArgs> ErrorsChanged;

        /// <summary>
        /// Raise the ErrorsChanged event for the specified property.
        /// </summary>
        protected void OnErrorsChanged(string propertyName)
        {
            ErrorsChanged?.Invoke(this, new DataErrorsChangedEventArgs(propertyName));
        }

        /// <summary>
        /// Get validation errors for a given property.
        /// </summary>
        public IEnumerable GetErrors(string propertyName)
        {
            if (string.IsNullOrEmpty(propertyName))
                return null;

            return _errors.TryGetValue(propertyName, out var errors) ? errors : null;
        }

        /// <summary>
        /// Add a validation error for a given property, if not already present.
        /// </summary>
        private void AddError(string propertyName, string error)
        {
            if (!_errors.ContainsKey(propertyName))
                _errors[propertyName] = new List<string>();

            if (!_errors[propertyName].Contains(error))
            {
                _errors[propertyName].Add(error);
                OnErrorsChanged(propertyName);
            }
        }

        /// <summary>
        /// Clear all validation errors for a given property.
        /// </summary>
        private void ClearErrors(string propertyName)
        {
            if (_errors.Remove(propertyName))
            {
                OnErrorsChanged(propertyName);
            }
        }

        #endregion

        #region Validation Hooks (Property Change Interceptors)

        partial void OnTickerChanged(string value)
        {
            ClearErrors(nameof(Ticker));
            if (string.IsNullOrWhiteSpace(value))
                AddError(nameof(Ticker), "Ticker is required.");
        }

        partial void OnCounterPartyIdChanged(int value)
        {
            ClearErrors(nameof(CounterPartyId));
            if (value <= 0)
                AddError(nameof(CounterPartyId), "CounterParty ID must be greater than zero.");
        }

        partial void OnCusipChanged(string value)
        {
            ClearErrors(nameof(Cusip));
            if (string.IsNullOrWhiteSpace(value))
                AddError(nameof(Cusip), "CUSIP is required.");
        }

        partial void OnPriceChanged(decimal value)
        {
            ClearErrors(nameof(Price));
            if (value <= 0)
                AddError(nameof(Price), "Price must be greater than zero.");
        }

        partial void OnBuySellChanged(string value)
        {
            ClearErrors(nameof(BuySell));

            if (string.IsNullOrWhiteSpace(value))
            {
                AddError(nameof(BuySell), "Buy/Sell value is required.");
                return;
            }

            var acceptableValues = new List<string> { "B", "S" };
            if (!acceptableValues.Any(v => v.Equals(value, StringComparison.OrdinalIgnoreCase)))
            {
                AddError(nameof(BuySell), "Buy/Sell value must be 'B' or 'S'.");
            }
        }

        partial void OnSpreadChanged(decimal value)
        {
            ClearErrors(nameof(Spread));
            if (value <= 0)
                AddError(nameof(Spread), "Spread must be greater than zero.");
        }

        partial void OnNotionalChanged(decimal value)
        {
            ClearErrors(nameof(Notional));
            if (value <= 0)
                AddError(nameof(Notional), "Notional must be greater than zero.");
        }

        #endregion
    }
}
