using AutoMapper;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DevExpress.Mvvm;
using LoanDbModel;
using Microsoft.IdentityModel.Protocols;
using OMS.Loans.Common;
using OMS.Loans.Common.DTO;
using OMS.Loans.Infrastructure;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace OMS.Loans.ViewModels
{
    public partial class TradeEntryViewModel : ObservableValidator, ISupportServices
    {
        IServiceContainer serviceContainer = null;
        private readonly ITradeEntries tradeEntriesService;
        private readonly ICounterParties counterPartiesService;
        private readonly IBlotterEntries blotterEntries;
        private readonly IMapper mapper;
        private bool userEnteredTradeId = true;

        [ObservableProperty]
        public ObservableCollection<string> tradeAccounts;
        [ObservableProperty]
        public ObservableCollection<string> strategies;
        [ObservableProperty]
        public ObservableCollection<string> subStrategies;
        [ObservableProperty]
        public bool isInteracted;
        [ObservableProperty]
        public ObservableCollection<CounterParty> counterParties;
        [ObservableProperty]
        public TradeEntryItem tradeEntryItem;
        [ObservableProperty]
        public decimal total;

        private HashSet<string> _fieldsToSum = new();

        [ObservableProperty]
        private ObservableCollection<AttachedDocument> documents = new();

        public TradeEntryViewModel()
        {
            //default ctor for automapper;    
        }

        public TradeEntryViewModel(ITradeEntries tradeEntriesService, ICounterParties counterPartiesService, IBlotterEntries blotterEntries, IMapper mapper, ITradeDocumentRetriever documentRetrieverService)
        {
            if (tradeEntriesService is null)
            {
                throw new ArgumentNullException(nameof(tradeEntriesService));
            }

            if (counterPartiesService is null)
            {
                throw new ArgumentNullException(nameof(counterPartiesService));
            }

            if (blotterEntries is null)
            {
                throw new ArgumentNullException(nameof(blotterEntries));
            }

            if (mapper is null)
            {
                throw new ArgumentNullException(nameof(mapper));
            }

            this.TradeAccounts = new() { "CBNY", "CBLON", "PAR", "TOK" };
            this.Strategies = new() { "FICC", "EQ", "SWAP" };
            this.SubStrategies = new() { "CONVERTIBLE", "RATES", "RATES/EQ" };
            this.tradeEntriesService = tradeEntriesService;
            this.counterPartiesService = counterPartiesService;
            this.blotterEntries = blotterEntries;
            this.mapper = mapper;
            DocumentRetrieverService = documentRetrieverService;
            this.counterParties = new();
            TradeEntryItem = new TradeEntryItem(counterPartiesService);
            Subscribe();

            _fieldsToSum.Add(nameof(tradeEntryItem.Notional));
            _fieldsToSum.Add(nameof(tradeEntryItem.CommitmentReduction));
            _fieldsToSum.Add(nameof(tradeEntryItem.Price));
            _fieldsToSum.Add(nameof(tradeEntryItem.FeesCosts));
            _fieldsToSum.Add(nameof(tradeEntryItem.InterestReceived));
            _fieldsToSum.Add(nameof(tradeEntryItem.EconomicBenefit));
            _fieldsToSum.Add(nameof(tradeEntryItem.DelayCompensation));

        }

        [RelayCommand]
        public async Task InitializeViewModel()
        {
            try
            {
                counterPartiesService
                    .GetCounterParties()
                    .ToList()
                    .ForEach(cp => CounterParties.Add(cp));
                BookTradeCommand.NotifyCanExecuteChanged();
                await Task.CompletedTask;
            }
            catch (Exception e)
            {

            }
        }


        [RelayCommand]
        public void LoadTradeRelatedData()
        {
            try
            {
                if (this.TradeEntryItem != null && this.TradeEntryItem.TradeId > 0)
                {
                    var result = tradeEntriesService.Get(TradeEntryItem.TradeId);
                    if (result == null)
                    {
                        MessageBoxService.ShowMessage($"TradeId {TradeEntryItem.TradeId} not found.");
                        return;
                    }

                    TradeEntryItem = mapper.Map<Trade, TradeEntryItem>(result);
                    Subscribe();
                  var documents=  DocumentRetrieverService.GetTradeDocuments(TradeEntryItem.TradeId).ToList();
                    foreach(var d in documents)
                    {
                        var doc = new AttachedDocument() { };
                        doc.Name =d.FileName;
                        doc.Data = new byte[d.Data.Length];
                        doc.isInDb = true;
                        Array.Copy(d.Data, doc.Data,d.Data.Length);
                        Documents.Add(doc);
                    }
                }

            }
            catch (Exception e)
            {
                MessageBoxService.ShowMessage("Failed to load attached document from database.");
            }

        }

        [RelayCommand]
        public void AttachDocumentation()
        {
            var fds = FileDialogService;
            FileDialogService.ShowDialog();
            var doc = fds.File;
            try
            {
               
                if (doc != null)
                {
                    var ad = new AttachedDocument()
                    {
                        Name = doc.Name,
                        FullPath = doc.GetFullName(),

                    };
                    ad.Data = File.ReadAllBytes(doc.GetFullName());
                    Documents.Add(ad);
                }
            }
            catch (Exception e)
            {

                MessageBoxService.ShowMessage($"Failed to attach {doc.GetFullName} + {e.Message}");
            }
           

        }

        public IMessageBoxService MessageBoxService => this.ServiceContainer.GetService<IMessageBoxService>();
        public IOpenFileDialogService FileDialogService => this.ServiceContainer.GetService<IOpenFileDialogService>();
        public IDialogService AccrualsDialogService()
        {
            return this.ServiceContainer.GetService<IDialogService>();
        }

        public bool CanBookTrade()
        {
            this.TradeEntryItem.Validate();
            var doesNotHaveErrors = !this.TradeEntryItem.HasErrors;
            return doesNotHaveErrors;
            /*
             * bool result = this.tradeEntryItem switch
            {
                { HasErrors:true } => false,
                { ticker: "TSLA" } => false,
                { counterParty: { CounterPartyCode: "Goldman Sachs" } } => false,
               
                { cusip: not null and var c } when c.StartsWith("912") => false,
                null => false,
                _ => false
            };
             */

        }

        [RelayCommand]
        public void BookTrade()
        {
            try
            {

                if (!CanBookTrade())
                {
                    return;
                }

                var trade = mapper.Map<TradeEntryItem, Trade>(this.TradeEntryItem);

                trade.TradeDocuments = new Collection<TradeDocument>();

                if (Documents.Any())
                {
                    var docs = Documents.Where(d=>d.isInDb==false).Select(doc => new TradeDocument()
                    {
                        ContentType = Path.GetExtension(doc.FullPath),
                        FileName = doc.Name,
                        Data = doc.Data,
                    });
                    foreach (var doc in docs)
                    {
                        trade.TradeDocuments.Add(doc);
                    }
                }


                if (trade.TradeId != 0)
                {
                    tradeEntriesService.Update(trade);

                    MessageBoxService.ShowMessage($"Trade Updated");
                }
                else
                {
                    var val = tradeEntriesService.Save(trade);
                    this.TradeEntryItem.tradeId = val;
                    MessageBoxService.ShowMessage($"Trade booked. Id{val}");
                }


            }
            catch (Exception e)
            {
                MessageBoxService.ShowMessage($"Trade failed to book {e.Message}");
            }
        }

        public IServiceContainer ServiceContainer
        {
            get
            {
                if (serviceContainer == null)
                    serviceContainer = new ServiceContainer(this);
                return serviceContainer;
            }
        }

        public ITradeDocumentRetriever DocumentRetrieverService { get; }

        public void Subscribe()
        {

            this.tradeEntryItem.PropertyChanged += TradeEntryItem_PropertyChanged;
        }

        [RelayCommand]
        public void ShowAccruals()
        {
            var viewModel = (AccrualEntriesViewModel)App.Current.Services.GetService(typeof(AccrualEntriesViewModel));
            viewModel.SetTradeId(this.TradeEntryItem.TradeId);
            var accrualService = AccrualsDialogService();
            
            accrualService.ShowDialog(dialogButtons: MessageButton.OKCancel, title: "Trade Accruals", viewModel: viewModel);
        }

        private void TradeEntryItem_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            BookTradeCommand.NotifyCanExecuteChanged();
            if (_fieldsToSum.Contains(e.PropertyName))
            {
                Total = TradeEntryItem.GetTotal();
            }
        }
    }

}
