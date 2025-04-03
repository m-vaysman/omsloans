using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;

namespace OMS.Loans.ViewModels
{
    public partial class BlotterItem : ObservableObject, INotifyDataErrorInfo

    {
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

        #region Validation
        private readonly Dictionary<string, List<string>> _errors = new();

        public bool HasErrors => _errors.Count > 0;

        public event EventHandler<DataErrorsChangedEventArgs> ErrorsChanged;

        protected void OnErrorsChanged(string propertyName)
        {
            ErrorsChanged?.Invoke(this, new DataErrorsChangedEventArgs(propertyName));
        }

        public IEnumerable GetErrors(string propertyName)
        {
            if (string.IsNullOrEmpty(propertyName))
                return null;

            return _errors.TryGetValue(propertyName, out var errors) ? errors : null;
        }

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

        private void ClearErrors(string propertyName)
        {
            if (_errors.Remove(propertyName))
            {
                OnErrorsChanged(propertyName);
            }
        }

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
            if (string.IsNullOrWhiteSpace(value)) {
                AddError(nameof(BuySell), "Buy/Sell value is required");
                return;
            }
            var acceptableValues = new List<string>() { "B","S"};
            if(!acceptableValues.Any(v => v.Equals(BuySell, StringComparison.OrdinalIgnoreCase))) { 
                AddError(nameof(BuySell), "Buy/Sell value must be 'B' or 'S'");
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
    }
#endregion
}
