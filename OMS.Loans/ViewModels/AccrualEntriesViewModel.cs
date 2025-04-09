using AutoMapper;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DevExpress.Mvvm;
using LoanDbModel;
using OMS.Loans.Common;
using OMS.Loans.Common.DTO;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace OMS.Loans.ViewModels
{
    public partial class AccrualEntriesViewModel:ObservableValidator, ISupportServices
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

        [ObservableProperty]
        public ObservableCollection<AccrualEntryItemViewModel> accrualEntryItems;
       
        private readonly IAccrualEntries accrualEntriesService;
        private readonly ITradeBalanceVsAccrued tradeBalanceVsAccrued;
        private ObservableCollection<DailyBalanceVsAccrued> dailyBalanceVsAccrued = new();
        private int tradeId;
        public IMapper Mapper { get; }


        public AccrualEntriesViewModel()
        {

        }


        public AccrualEntriesViewModel(IAccrualEntries accrualEntriesService,IMapper mapper, ITradeBalanceVsAccrued tradeBalanceVsAccrued):this()
        {
            if (mapper is null)
            {
                throw new ArgumentNullException(nameof(mapper));
            }

            if (tradeBalanceVsAccrued is null)
            {
                throw new ArgumentNullException(nameof(tradeBalanceVsAccrued));
            }

            AccrualEntryItems = new();
            var items=accrualEntryItems as ObservableCollection<AccrualEntryItemViewModel>;
            items.CollectionChanged += Items_CollectionChanged;
            this.accrualEntriesService = accrualEntriesService ?? throw new ArgumentNullException(nameof(accrualEntriesService));
            
            Mapper = mapper;
            this.tradeBalanceVsAccrued = tradeBalanceVsAccrued;
           
        }

        private void Items_CollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Add)
            {
                foreach(var i in e.NewItems)
                {
                    var item = i as AccrualEntryItemViewModel;
                    item.TradeId = this.tradeId;
                }
            }
        }

        [RelayCommand]
        public void InitializeViewModel()
        {
            if (tradeId == 0)
            {

            }
            //make this into base class
            var result = from a in accrualEntriesService.GetAccruals(tradeId)
                         select new {item= Mapper.Map<Accrual, AccrualEntryItemViewModel>(a) } ;
            
            result.ToList().ForEach(i =>
            {
                try
                {
                    //so calc binding is updated.
                    i.item.RecalculateExpectedAccrualCash();
                    accrualEntryItems.Add(i.item);
                }
                catch(Exception e) {
                //do nothing.
                }
            
            });

            if (result.Any()) {

                tradeBalanceVsAccrued.GetDailyBalanceVsAccrued(result.First().item.TradeId)
                    .ToList()
                    .ForEach(b=>dailyBalanceVsAccrued.Add(b));
            
            }

        }

        public void SetTradeId(int tradeId) => this.tradeId = tradeId;

        [RelayCommand]
        public void SaveAccruals()
        {
            try
            {
                

                foreach (var entry in this.AccrualEntryItems)
                {
                    var accrualEntity = Mapper.Map<AccrualEntryItemViewModel, Accrual>(entry);

                    if (accrualEntity.AccrualId != 0)
                    {
                        this.accrualEntriesService.Update(accrualEntity);
                    }
                    else
                    {
                        var id = this.accrualEntriesService.SaveAccrual(accrualEntity);
                    }
                }
                MessageBoxService.ShowMessage("Accruals updated");
            }
            catch (Exception ex)
            {
                MessageBoxService.ShowMessage(ex.Message);
            }

        }

        public IMessageBoxService MessageBoxService => ServiceContainer.GetService<IMessageBoxService>();
    }
}
