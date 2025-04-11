using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using DevExpress.Xpf.Grid;
using OMS.Loans.Infrastructure;
using OMS.Loans.Message;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Printing;
using System.Text;
using System.Threading.Tasks;

namespace OMS.Loans.ViewModels
{
    public partial class ExternalCashFlowViewModel : ObservableValidator
    {
        [ObservableProperty]
        private ObservableCollection<ExternalCashFlowItemViewModel> externalCashFlowEntries=new();

        [ObservableProperty]
        private ExternalCashFlowItemViewModel selectedItem;


        public ExternalCashFlowViewModel()
        {
            WeakReferenceMessenger.Default.Register<RejectedExternalCashFlowItemMessage>(this, RejectedExpectedCashFlowItemMessage_Handler);
        }

        private void RejectedExpectedCashFlowItemMessage_Handler(object recipient, RejectedExternalCashFlowItemMessage message)
        {
            ExternalCashFlowEntries.Add(message.Value);
        }

        partial void OnSelectedItemChanged(ExternalCashFlowItemViewModel value)
        {
            SplitCommand.NotifyCanExecuteChanged();
            RemoveSplitCommand.NotifyCanExecuteChanged();
            PublishExternalCashFlowItemCommand.NotifyCanExecuteChanged();
        }

        public bool CanSplit() => selectedItem != null ? selectedItem.IsParent : false;

        public bool CanPublishExternalCashFlowItem()
        {
            return SelectedItem != null;
        }

        [RelayCommand(CanExecute =nameof(CanPublishExternalCashFlowItem))]
        public void PublishExternalCashFlowItem()
        {
            WeakReferenceMessenger.Default.Send<ExternalCashFlowItemMessage>(new ExternalCashFlowItemMessage(selectedItem));
            ExternalCashFlowEntries.RemoveByReference(selectedItem);
        }

        [RelayCommand(CanExecute ="CanSplit")]
        public void Split() {

           var item= selectedItem.CreateChildItem();
            externalCashFlowEntries.Add(item);
            
        }

        public bool CanRemoveSplit() => selectedItem!=null?!selectedItem.IsParent:false;


        [RelayCommand(CanExecute ="CanRemoveSplit")]
        public void RemoveSplit()
        {
          var itemToRemove=  ExternalCashFlowEntries.Select((x, i) => new { index = i, item = x }).First(i => object.ReferenceEquals(selectedItem, i.item));

          var parentModel=  ExternalCashFlowEntries.First(i => i.Code == selectedItem.Code);
            parentModel.Amount = selectedItem.Amount + parentModel.Amount;
            ExternalCashFlowEntries.RemoveAt(itemToRemove.index);
        }


    }
    public partial class ExternalCashFlowItemViewModel:ObservableValidator
    {

        public List<ChildExternalCashFlowViewModel> Children { get; set; } = new List<ChildExternalCashFlowViewModel>();
        public bool IsParent { get; }
        private ExternalCashFlowItemViewModel Parent { get; set; }

        [ObservableProperty]
        private string counterParty;
        [ObservableProperty]
        private string code;
        [ObservableProperty]
        private string subCode;
        [ObservableProperty]
        private DateOnly date;
        [ObservableProperty]
        private decimal amount;
        [ObservableProperty]
        private decimal amountToSplit;
        [RelayCommand]
        public void SetStatusToDk() { }
       
        [RelayCommand]
        public void Split() {
            var child = CreateChild();
            this.Children.Add(child);
        }

        public ExternalCashFlowItemViewModel(string counterParty, string code,DateOnly date,decimal amount)
        {
            this.CounterParty = counterParty;
            this.Code = code;
            this.Date = date;
            this.Amount = amount;
            this.IsParent = true;
        }

        public ExternalCashFlowItemViewModel()
        {
            
        }

        public ExternalCashFlowItemViewModel CreateChildItem()
        {

            var result = new ExternalCashFlowItemViewModel() { Parent = this,   CounterParty = this.CounterParty, Code = this.Code, SubCode = $"{this.Code}_{DateTime.Now.ToFileTimeUtc().ToString()}", Date = this.Date, Amount = this.AmountToSplit };
            this.Amount =this.Amount-  this.AmountToSplit;
            this.AmountToSplit = 0;
            return result;
            }
        private ChildExternalCashFlowViewModel CreateChild() => new ChildExternalCashFlowViewModel() { Parent = this, CounterParty = this.CounterParty, Code = $"{this.Code}_{DateTime.Now.ToFileTimeUtc}", Date = this.Date, Amount = this.Amount - this.AmountToSplit };
    }

    public  class ChildExternalCashFlowViewModel : ExternalCashFlowItemViewModel {

        new public RelayCommand SplitCommand { get; set; }
        new public decimal AmountTosplit { get; set; }
    
    }

    public class ExternalCashFlowChildNodeSelector : IChildNodesSelector
    {
        public IEnumerable SelectChildren(object item)
        {
            if (item is ExternalCashFlowItemViewModel)
                return (item as ExternalCashFlowItemViewModel).Children;

            return null;
        }
    }
}
