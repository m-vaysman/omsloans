using AutoMapper;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using DevExpress.Data.Mask.Internal;
using DevExpress.Mvvm;
using DevExpress.Xpf.Docking;
using LoanDbModel;
using Microsoft.IdentityModel.Tokens;
using OMS.Loans.Common;
using OMS.Loans.Message;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;

namespace OMS.Loans.ViewModels
{
    /// <summary>
    /// ViewModel for managing loan trade blotter entries in the WPF OMS.
    /// Implements MVVM using ObservableObject and DevExpress docking support.
    /// </summary>
    public partial class BlotterViewModel : ObservableObject, IMVVMDockingProperties, ISupportServices
    {
        IServiceContainer serviceContainer = null;

       
        public IServiceContainer ServiceContainer
        {
            get
            {
                if (serviceContainer == null)
                    serviceContainer = new ServiceContainer(this);
                return serviceContainer;
            }
        }
        private readonly LoanDbContext loanDbContext;
        private readonly ICounterParties counterPartyService;
        private readonly IMapper mapper;
        [ObservableProperty]
        public ObservableCollection<BlotterItem> blotterItems;
        [ObservableProperty]
        public BlotterItem blotterItem;
        [ObservableProperty]
        public ObservableCollection<CounterParty> counterParties;
        [ObservableProperty]
        public CounterParty selectedCounterParty;

        public BlotterViewModel(IMapper mapper)
        {
            this.mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));



            WeakReferenceMessenger.Default.Register<BlotteredTradeSelectedMessage>(this, (e, o) =>
            {

                this.BlotterItem = new BlotterItem();
                this.BlotterItem.PropertyChanged += BlotterItem_PropertyChanged;
                var m = mapper.Map<Blotter, BlotterItem>(o.BlotterItem);
                this.BlotterItem = m;
                this.SaveBlotterItemCommand.NotifyCanExecuteChanged();
            });
        }

        /// <summary>
        /// Responds to property changes in the current blotter item, e.g., for validation.
        /// </summary>
        private void BlotterItem_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            this.SaveBlotterItemCommand.NotifyCanExecuteChanged();
        }

        /// <summary>
        /// Called automatically when SelectedCounterParty changes.
        /// </summary>
        partial void OnSelectedCounterPartyChanged(CounterParty counterParty)
        {
            this.SaveBlotterItemCommand.NotifyCanExecuteChanged();
        }

        /// <summary>
        /// Full constructor with services and DB context injection.
        /// </summary>
        public BlotterViewModel(LoanDbContext loanDbContext, ICounterParties counterPartyService, IMapper mapper) : this(mapper)
        {
            BlotterItems = new ObservableCollection<BlotterItem>();
            CounterParties = new();
            this.loanDbContext = loanDbContext;
            this.counterPartyService = counterPartyService ?? throw new ArgumentNullException(nameof(counterPartyService));

            this.blotterItem = new BlotterItem();
            this.BlotterItem.PropertyChanged += BlotterItem_PropertyChanged;
            this.BlotterItem.BuySell = "B";
            this.BlotterItem.TradeDate = DateTime.Now;
        }

        [RelayCommand]
        public void ViewModelSetUp()
        {
            try
            {
                if (CounterParties.Count == 0)
                {
                    counterPartyService.GetCounterParties().ToList().ForEach(cp => CounterParties.Add(cp));
                }
            }
            catch (Exception e)
            {
                MessageBoxService.ShowMessage(e.Message, "Error", MessageButton.OK, MessageIcon.Error);
            }
        }

        
        private bool CanSaveBlotterItem()
        {
            if (BlotterItem == null)
                return false;
            if (SelectedCounterParty == null)
                return false;
            if (SelectedCounterParty.CounterPartyName.IsNullOrEmpty())
                return false;

            if (BlotterItem.Ticker.IsNullOrEmpty() || BlotterItem.TradeAcct.IsNullOrEmpty())
                return false;

           return !BlotterItem.HasErrors;
        } 

        [RelayCommand(CanExecute = nameof(CanSaveBlotterItem))]
        public void SaveBlotterItem()
        {

            try
            {
                var blotter = new Blotter();

                
                blotter.CounterPartyId = SelectedCounterParty.CounterPartyId;
                blotter.BuySell = BlotterItem.BuySell;
                blotter.Ticker = BlotterItem.Ticker;
                blotter.TradeAcct = BlotterItem.TradeAcct;
                blotter.TradeDate = BlotterItem.TradeDate;
                blotter.Price = BlotterItem.Price;
                blotter.Spread = BlotterItem.Spread;
                blotter.Notional = BlotterItem.Notional;
                loanDbContext.Add(blotter);
                loanDbContext.SaveChanges();
                SelectedCounterParty = null;
                MessageBoxService.ShowMessage($"Blotter booked. Id:{blotter.BlotterId}");
                WeakReferenceMessenger.Default.Send(new TradeBlotteredMessage() { BlotteredTrade = BlotterItem });
            }
            catch (Exception e)
            {
                MessageBoxService.ShowMessage(e.Message, "Error", MessageButton.OK, MessageIcon.Error);

            }

            this.BlotterItem = new BlotterItem();
            this.BlotterItem.PropertyChanged += BlotterItem_PropertyChanged;
        }

        /// <summary>
        /// Provides access to DevExpress message box service for UI alerts.
        /// </summary>
        public IMessageBoxService MessageBoxService => ServiceContainer.GetService<IMessageBoxService>();

        /// <summary>
        /// Required for DevExpress docking integration.
        /// </summary>
        public string TargetName { get => "DockPanels"; set => throw new NotImplementedException(); }


    }
}
