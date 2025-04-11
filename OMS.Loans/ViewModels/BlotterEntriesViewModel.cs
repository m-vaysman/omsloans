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
    /// Implements MVVM using CommunityToolkit and integrates with DevExpress docking.
    /// </summary>
    public partial class BlotterEntriesViewModel : ObservableObject, IMVVMDockingProperties
    {
        private readonly IBlotterEntries blotterEntriesService;

        /// <summary>
        /// Collection of blotter entries shown in the UI.
        /// Bound to a DevExpress data grid or other collection-based control.
        /// </summary>
        [ObservableProperty]
        public ObservableCollection<Blotter> blotterEntries;

        /// <summary>
        /// The currently selected blotter entry.
        /// </summary>
        [ObservableProperty]
        public Blotter selectedBlotterEntry;

        /// <summary>
        /// Default constructor that subscribes to TradeBlotteredMessage events.
        /// </summary>
        public BlotterEntriesViewModel()
        {
            // Register to receive messages when new trades are blottered
            WeakReferenceMessenger.Default.Register<TradeBlotteredMessage>(this, (r, message) =>
            {
                LoadBlotterEntries();
            });
        }

        /// <summary>
        /// Constructor for dependency injection.
        /// </summary>
        /// <param name="blotterEntriesService">Service to retrieve blotter entries from the database</param>
        public BlotterEntriesViewModel(IBlotterEntries blotterEntriesService) : this()
        {
            this.blotterEntriesService = blotterEntriesService;
            BlotterEntries = new ObservableCollection<Blotter>();
        }

        /// <summary>
        /// Called automatically when SelectedBlotterEntry changes (via toolkit-generated partial).
        /// Sends a message to notify that a trade has been selected.
        /// </summary>
        partial void OnSelectedBlotterEntryChanged(Blotter value)
        {
            if (value != null)
            {
                WeakReferenceMessenger.Default.Send(new BlotteredTradeSelectedMessage
                {
                    BlotterItem = value
                });
            }
        }

        /// <summary>
        /// Tells the DevExpress docking system which panel group to inject this view into.
        /// </summary>
        public string TargetName
        {
            get => "DockPanels";
            set => throw new NotImplementedException(); // Usually not used for MVVM-only apps
        }

        /// <summary>
        /// Loads all blotter entries and orders them by descending BlotterId (most recent first).
        /// </summary>
        [RelayCommand]
        public void LoadBlotterEntries()
        {
            BlotterEntries.Clear();

            blotterEntriesService.GetBlotterEntries()
                .OrderByDescending(e => e.BlotterId)
                .ToList()
                .ForEach(b => BlotterEntries.Add(b));
        }
    }
}
