using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DevExpress.Mvvm.POCO;
using LoanDbModel;
using Microsoft.IdentityModel.Tokens;
using OMS.Loans.Common;
using OMS.Loans.Infrastructure.Attributes;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Controls.Primitives;

namespace OMS.Loans.ViewModels
{
    public partial class TradeEntryItem : ObservableValidator //ObservableNotifyDataError
    {

        [ObservableProperty]
        public ObservableCollection<Paydown> paydowns;

        [ObservableProperty]
        public int tradeId;

        [ObservableProperty]
        public CounterParty counterParty;

        [ObservableProperty]
        [NotifyDataErrorInfo]
        [Range(1, int.MaxValue, ErrorMessage = "CounterPartyId must be greater than 0.")]
        public int counterPartyId;

        [ObservableProperty]
        [Required(AllowEmptyStrings =false,ErrorMessage ="Ticker must be supplied")]
        public string ticker;

        [ObservableProperty]
        public string cusip;

        [ObservableProperty]
        public DateTime tradeDate;

        [ObservableProperty]
    
        public DateTime settlementDate;

        [ObservableProperty]
        [NotifyDataErrorInfo]
        [BuySellRequired]
        public string buySell;

        [ObservableProperty]
        [NotifyDataErrorInfo]
        [Range(0,double.MaxValue,ErrorMessage="Price must be above 0")]
        public decimal price;

        [ObservableProperty]
        public decimal globalCommitment;

        [ObservableProperty]
        [NotifyDataErrorInfo]
        [Range(1,double.MaxValue,ErrorMessage ="Notional cannot be less than 0")]
        public decimal notional;

        [ObservableProperty]
        public decimal termUnfundedAmount;

        [ObservableProperty]
        public decimal commitmentReduction;

        [ObservableProperty]
        public decimal feesCosts;

        [ObservableProperty]
        public decimal delayCompensation;

        [ObservableProperty]
        public decimal interestReceived;

        [ObservableProperty]
        public decimal additionalCredit;

        [ObservableProperty]
        public decimal economicBenefit;

        [ObservableProperty]
        public string tradeAcct;

        [ObservableProperty]
        public string? strategy;

        [ObservableProperty]
        public string? subStrategy;

        [ObservableProperty]
        public string currency="USD";

        [ObservableProperty]
        public decimal spread;

        [ObservableProperty]
        public bool isSettled;

        private readonly ICounterParties counterPartiesService;

        public TradeEntryItem(ICounterParties counterPartiesService):this()
        {
            if (counterPartiesService is null)
            {
                throw new ArgumentNullException(nameof(counterPartiesService));
            }
            
            this.counterPartiesService = counterPartiesService;
        }

        public TradeEntryItem()
        {
            SettlementDate = DateTime.Now.AddDays(1);
            TradeDate = DateTime.Now;
            Price = 1;
            OnTickerChangedCommand= new RelayCommand<string>(OnTickerChanged);
            BuySell = "B";
            Cusip = "n";
        }


        public RelayCommand<string> OnTickerChangedCommand { get; }
      
        partial void OnNotionalChanged(decimal value) {
            //  ValidateProp(this, p => p.Notional, c => c.Notional < 1, "Notional cannot be less than 1");
            ValidateProperty(value, nameof(Notional));    
                
            
        }
       partial void OnTickerChanged(string value)
        {

            ValidateProperty(value,nameof(Ticker));
         //   ValidateProp(this, p => p.Ticker, c => value.IsNullOrEmpty(), "Ticker can not be empty");

                    }

     
        partial void OnTradeDateChanged(DateTime value)
        {
          //  ValidateProp(this, p => p.TradeDate, p => p.TradeDate == default(DateTime), "Date cannot be a default value");
        }

        partial void OnSettlementDateChanged(DateTime value) {
          //  ValidateProp(this, p => p.SettlementDate, p => p.SettlementDate < p.TradeDate, $"SettlementDate {value} cannot be less than TradeDate {this.TradeDate}");
        }

        partial void OnPriceChanged(decimal value)
        {
            ValidateProperty(value, nameof(Price));
          //  ValidateProp(this, p => p.Price, p => p.Price <= 0, "Price cannot be less than or equal to zero");
        }

        partial void OnSpreadChanged(decimal value)
        {
          //  ValidateProp(this, p => p.Spread, p => p.Spread < 0, "Spread cannot be less than zero");
        }

        partial void OnBuySellChanged(string value)
        {
         //   ValidateProp(this, p => p.Notional, p => p.BuySell == "B" && p.Notional < 0, "Notional must be positive when trade is set to Buy")
          //      .ValidateProp(this, p => p.Notional, p => p.BuySell == "S" && p.Notional > 0, "Notional must be a negative value if trade is set to Sell");
        }

        partial void OnCounterPartyIdChanged(int value)
        {
            ValidateProperty(value, nameof(CounterPartyId));
        }

        partial void OnCounterPartyChanged(CounterParty value)
        {
            if (CounterParty != null)
            {
                CounterPartyId = CounterParty.CounterPartyId;
            }

        }

        partial void OnTradeAcctChanged(string value)
        {
            var e = value;
        }

        public decimal GetTotal()
        {
          return  ((Notional - CommitmentReduction) * Price) + FeesCosts - InterestReceived + EconomicBenefit + DelayCompensation+AdditionalCredit;
        }

        public void Validate() {
            ValidateAllProperties();
        }
  
    }
}
