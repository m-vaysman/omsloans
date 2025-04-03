using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using DevExpress.Xpf.Docking;
using LoanDbModel;
using OMS.Loans.Common;
using OMS.Loans.Message;
using System;
using System.Collections.ObjectModel;
using System.Linq;

namespace OMS.Loans.ViewModels
{
    /// <summary>
    /// ViewModel responsible for displaying and managing a list of blotter trade entries.
    /// Implements MVVM binding using CommunityToolkit and DevExpress docking support.
    /// </summary>
    public partial class BlotterEntriesViewModel : ObservableObject, IMVVMDockingProperties
    {
        private readonly IBlotterEntries blotterEntriesService;

        [ObservableProperty]
        public ObservableCollection<Blotter> blotterEntries;

        [ObservableProperty]
        public Blotter selectedBlotterEntry;


        public BlotterEntriesViewModel()
        {
            WeakReferenceMessenger.Default.Register<TradeBlotteredMessage>(this, (r, message) =>
            {

                LoadBlotterEntries();

            });
        }

        partial void OnSelectedBlotterEntryChanged(Blotter value)
        {
            if (value != null)
            {
                WeakReferenceMessenger.Default.Send<BlotteredTradeSelectedMessage>(new BlotteredTradeSelectedMessage() { BlotterItem = value });
            }
        }

        public BlotterEntriesViewModel(IBlotterEntries blotterEntriesService) : this()
        {
            this.blotterEntriesService = blotterEntriesService;
            BlotterEntries = new();
        }

        /// <summary>
        /// This property will tell the doc into which area the view should be injected.
        /// </summary>
        public string TargetName { get => "DockPanels"; set => throw new NotImplementedException(); }

        [RelayCommand]
        public void LoadBlotterEntries()
        {
            blotterEntries.Clear();
            blotterEntriesService.GetBlotterEntries().OrderByDescending(e => e.BlotterId).ToList().ForEach(b => { BlotterEntries.Add(b); });
        }

    }
}
