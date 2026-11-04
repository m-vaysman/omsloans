using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OMS.Loans.Infrastructure;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace OMS.Loans.ViewModels
{
    /// <summary>
    /// ViewModel responsible for managing and merging expected cash flow and external cash receipt items.
    /// Combines both into a single matching collection and supports commands for applying or removing matches.
    /// </summary>
    public partial class ExpectedCashFlowItemMergedViewModel : ObservableObject
    {
        // Collection of merged cash match items (combined from expected and received flows)
        [ObservableProperty]
        private ObservableCollection<CashMatchItemViewModel> cashMatchingItems = new();

        // Collection of expected (outgoing) cash flow items
        [ObservableProperty]
        private ObservableCollection<ExpectedCashFlowItemViewModel> expectedCash = new();

        // Collection of external (incoming) cash receipts
        [ObservableProperty]
        private ObservableCollection<ExternalCashFlowItemViewModel> cashReceipts = new();

        // Total expected cash (outgoing)
        [ObservableProperty]
        private decimal expectedCashTotal;

        // Total incoming cash (receipts)
        [ObservableProperty]
        private decimal cashReceiptsTotal;

        // Flag indicating if any items are in the matching collection
        [ObservableProperty]
        private bool collectionIsNotEmpty;

        // The currently selected item in the UI (from the match list)
        [ObservableProperty]
        private CashMatchItemViewModel selectedItem;

        [ObservableProperty]
        private ExpectedCashFlowItemViewModel selectedExpected;

        [ObservableProperty]
        private ExternalCashFlowItemViewModel selectedExternal;

        public ExpectedCashFlowItemMergedViewModel()
        {
            cashMatchingItems.CollectionChanged += (_, _) =>
            {
                CollectionIsNotEmpty = cashMatchingItems.Count > 0;
                ApplyCashCommand.NotifyCanExecuteChanged();
            };

            CollectionIsNotEmpty = false;
            ApplyCashCommand.NotifyCanExecuteChanged();
        }

        [RelayCommand]
        public void PublishExpected(ExpectedCashFlowItemViewModel item)
        {
            item ??= SelectedExpected;
            if (item == null)
            {
                return;
            }

            CashMatchingItems.Add(new CashMatchItemViewModel(item));
            ExpectedCash.RemoveByReference(item);
        }

        [RelayCommand]
        public void PublishExternal(ExternalCashFlowItemViewModel item)
        {
            item ??= SelectedExternal;
            if (item == null)
            {
                return;
            }

            CashMatchingItems.Add(new CashMatchItemViewModel(item));
            CashReceipts.RemoveByReference(item);
        }

        [RelayCommand]
        public void Split(ExternalCashFlowItemViewModel item)
        {
            item ??= SelectedExternal;
            if (item == null || !item.IsParent)
            {
                return;
            }

            CashReceipts.Add(item.CreateChildItem());
        }

        [RelayCommand]
        public void RemoveSplit(ExternalCashFlowItemViewModel item)
        {
            item ??= SelectedExternal;
            if (item == null || item.IsParent)
            {
                return;
            }

            var parent = CashReceipts.FirstOrDefault(candidate => candidate.IsParent && candidate.Code == item.Code);
            if (parent == null)
            {
                return;
            }

            parent.Amount += item.Amount;
            CashReceipts.RemoveByReference(item);
        }

        /// <summary>
        /// Determines if the ApplyCash command is allowed:
        /// Must have items, and their net total must be zero.
        /// </summary>
        public bool CanApplyCash()
        {
            if (cashMatchingItems.Count == 0)
                return false;

            return cashMatchingItems.Sum(i => i.Amount) == 0;
        }

        /// <summary>
        /// Command for applying cash matches. Only enabled when `CanApplyCash` is true.
        /// </summary>
        [RelayCommand(CanExecute = nameof(CanApplyCash))]
        public void ApplyCash()
        {
            var path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "OMS.Loans",
                "cash-matches.json");
            Directory.CreateDirectory(Path.GetDirectoryName(path));

            var prior = new List<CashMatchBatch>();
            if (File.Exists(path))
            {
                prior = JsonSerializer.Deserialize<List<CashMatchBatch>>(File.ReadAllText(path)) ?? prior;
            }

            prior.Add(new CashMatchBatch(
                DateTime.UtcNow,
                CashMatchingItems.Select(item => new CashMatchRecord(
                    item.SourceType,
                    item.Date,
                    item.Source,
                    item.CounterParty,
                    item.Code,
                    item.Amount)).ToList()));
            File.WriteAllText(path, JsonSerializer.Serialize(prior));
            CashMatchingItems.Clear();
        }

        [RelayCommand]
        public void RemoveMatch(CashMatchItemViewModel item)
        {
            item ??= SelectedItem;
            if (item == null)
            {
                return;
            }

            if (item.OriginalVm is ExternalCashFlowItemViewModel externalVm)
            {
                CashReceipts.Add(externalVm);
            }
            else if (item.OriginalVm is ExpectedCashFlowItemViewModel expectedVm)
            {
                ExpectedCash.Add(expectedVm);
            }

            CashMatchingItems.RemoveByReference(item);
        }
    }

    public record CashMatchRecord(string SourceType, DateOnly Date, string Source, string CounterParty, string Code, decimal Amount);

    public record CashMatchBatch(DateTime AppliedAtUtc, List<CashMatchRecord> Lines);
}
