using OMS.Loans.ViewModels;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace OMS.Loans.Views
{
    /// <summary>
    /// Interaction logic for ExternalCashFlowView.xaml
    /// </summary>
    public partial class ExternalCashFlowView : UserControl
    {
        public ExternalCashFlowView()
        {
            InitializeComponent();
            var vm = new ExternalCashFlowItemViewModel("BOA","EW2323", new DateOnly(2025, 1, 14),40000.0m);
            var vm2 = new ExternalCashFlowItemViewModel("JPM", "EW232213", new DateOnly(2025, 2, 11),43344.32m);
            var vm3 = new ExternalCashFlowItemViewModel("WAC", "EW231223", new DateOnly(2025, 4, 4),2322.22m);
            var vm4 = new ExternalCashFlowItemViewModel("BOA", "EW322323", new DateOnly(2025, 1, 1),2300.43m);

            var col = new ObservableCollection<ExternalCashFlowItemViewModel>() { vm,vm2,vm3,vm4};

            var mainmode = new ExternalCashFlowViewModel();
            mainmode.ExternalCashFlowEntries = col;
            this.DataContext = mainmode;
        }
    }
}
