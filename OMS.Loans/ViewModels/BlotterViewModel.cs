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
    /// ViewModel for managing loan blotter entries.
    /// Supports MVVM design, validation, command enablement, AutoMapper integration, and messaging.
    /// </summary>
    public partial class BlotterViewModel : ObservableObject, IMVVMDockingProperties, ISupportServices
    {
        private IServiceContainer serviceContainer = null;

        /// <summary>
        /// Provides access to DevExpress service container for resolving UI services like IMessageBoxService.
        /// </summary>
        public IServiceContainer ServiceContainer
        {
            get
            {
                if (serviceContainer == null)
                    serviceContainer = new ServiceContainer(this);
                return serviceContainer;
            }
        }

        // Dependencies
        private readonly LoanDbContext loanDbContext;
        private readonly ICounterParties counterPartyService;
        private readonly IMapper mapper;

        // Bindable collections and selected values
        [ObservableProperty]
        public ObservableCollection<BlotterItem> blotterItems;

        [ObservableProperty]
        public BlotterItem blotterItem;

        [ObservableProperty]
        public ObservableCollection<CounterParty> counterParties;

        [ObservableProperty]
        public CounterParty selectedCounterParty;

        /// <summary>
        /// Constructor for design-time/test use with mapper only.
        /// Registers for Trade selection messages.
        /// </summary>
        public BlotterViewModel(IMapper mapper)
        {
            this.mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));

            // Listen for trade selection and initialize BlotterItem
            WeakReferenceMessenger.Default.Register<BlotteredTradeSelectedMessage>(this, (e, o) =>
            {
                this.BlotterItem = new BlotterItem();
                this.BlotterItem.PropertyChanged += BlotterItem_PropertyChanged;

                // Map incoming domain model to editable ViewModel
                var m = mapper.Map<Blotter, BlotterItem>(o.BlotterItem);
                this.BlotterItem = m;

                SaveBlotterItemCommand.NotifyCanExecuteChanged();
            });
        }

        /// <summary>
        /// Constructor with service and DbContext injection.
        /// </summary>
        public BlotterViewModel(LoanDbContext loanDbContext, ICounterParties counterPartyService, IMapper mapper)
            : this(mapper)
        {
            this.loanDbContext = loanDbContext;
            this.counterPartyService = counterPartyService ?? throw new ArgumentNullException(nameof(counterPartyService));

            BlotterItems = new ObservableCollection<BlotterItem>();
            CounterParties = new ObservableCollection<CounterParty>();

            this.BlotterItem = new BlotterItem
            {
                BuySell = "B",
                TradeDate = DateTime.Now
            };
            this.BlotterItem.PropertyChanged += BlotterItem_PropertyChanged;
        }

        /// <summary>
        /// Called when any property of the current blotter item changes.
        /// Used to re-evaluate Save command availability.
        /// </summary>
        private void BlotterItem_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            SaveBlotterItemCommand.NotifyCanExecuteChanged();
        }

        /// <summary>
        /// Updates Save command state when the selected counterparty changes.
        /// </summary>
        partial void OnSelectedCounterPartyChanged(CounterParty counterParty)
        {
            SaveBlotterItemCommand.NotifyCanExecuteChanged();
        }

        /// <summary>
        /// Loads counterparties into memory for dropdown binding.
        /// </summary>
        [RelayCommand]
        public void ViewModelSetUp()
        {
            try
            {
                if (CounterParties.Count == 0)
                {
                    var list = counterPartyService.GetCounterParties();
                    foreach (var cp in list)
                        CounterParties.Add(cp);
                }
            }
            catch (Exception e)
            {
                MessageBoxService.ShowMessage(e.Message, "Error", MessageButton.OK, MessageIcon.Error);
            }
        }

        /// <summary>
        /// Determines whether the Save command should be enabled.
        /// </summary>
        private bool CanSaveBlotterItem()
        {
            if (BlotterItem == null || SelectedCounterParty == null)
                return false;

            if (SelectedCounterParty.CounterPartyName.IsNullOrEmpty())
                return false;

            if (BlotterItem.Ticker.IsNullOrEmpty() || BlotterItem.TradeAcct.IsNullOrEmpty())
                return false;

            return !BlotterItem.HasErrors;
        }

        /// <summary>
        /// Saves the current blotter item to the database.
        /// </summary>
        [RelayCommand(CanExecute = nameof(CanSaveBlotterItem))]
        public void SaveBlotterItem()
        {
            try
            {
                var blotter = new Blotter
                {
                    CounterPartyId = SelectedCounterParty.CounterPartyId,
                    BuySell = BlotterItem.BuySell,
                    Ticker = BlotterItem.Ticker,
                    TradeAcct = BlotterItem.TradeAcct,
                    TradeDate = BlotterItem.TradeDate,
                    Price = BlotterItem.Price,
                    Spread = BlotterItem.Spread,
                    Notional = BlotterItem.Notional
                };

                loanDbContext.Add(blotter);
                loanDbContext.SaveChanges();

                SelectedCounterParty = null;

                MessageBoxService.ShowMessage($"Blotter booked. Id: {blotter.BlotterId}");

                // Notify other viewmodels that a blotter trade was saved
                WeakReferenceMessenger.Default.Send(new TradeBlotteredMessage
                {
                    BlotteredTrade = BlotterItem
                });
            }
            catch (Exception e)
            {
                MessageBoxService.ShowMessage(e.Message, "Error", MessageButton.OK, MessageIcon.Error);
            }

            // Reset the form for a new entry
            this.BlotterItem = new BlotterItem();
            this.BlotterItem.PropertyChanged += BlotterItem_PropertyChanged;
        }

        /// <summary>
        /// Provides access to DevExpress message box service for UI alerts.
        /// </summary>
        public IMessageBoxService MessageBoxService => ServiceContainer.GetService<IMessageBoxService>();

        /// <summary>
        /// Required by DevExpress to determine which dock panel this ViewModel binds to.
        /// </summary>
        public string TargetName
        {
            get => "DockPanels";
            set => throw new NotImplementedException();
        }
    }
}
