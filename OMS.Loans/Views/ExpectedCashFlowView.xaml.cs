using OMS.Loans.ViewModels;
using System;
using System.Collections.Generic;
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
    /// Interaction logic for ExpectedCashFlowView.xaml
    /// </summary>
    public partial class ExpectedCashFlowView : UserControl
    {
        public ExpectedCashFlowView()
        {
            InitializeComponent();
            var vm = new ExpectedCashFlowViewModel();
            vm.ExpectedCashFlowItems.Add(new ExpectedCashFlowItemViewModel() { Amount=500, Code ="ACC2023", CounterParty="BOA", ExpectedCashPaymentDate=new DateOnly(2025,1,10), Source="Loan Accrual"});
            vm.ExpectedCashFlowItems.Add(new ExpectedCashFlowItemViewModel() { Amount = 500, Code = "ACC2024", CounterParty = "BOA", ExpectedCashPaymentDate = new DateOnly(2025, 1, 10), Source = "Loan Accrual" });
            vm.ExpectedCashFlowItems.Add(new ExpectedCashFlowItemViewModel() { Amount = 33500, Code = "ACC2025", CounterParty = "JPM", ExpectedCashPaymentDate = new DateOnly(2025, 1, 10), Source = "Loan Accrual" });
            vm.ExpectedCashFlowItems.Add(new ExpectedCashFlowItemViewModel() { Amount = 1200, Code = "ACC2026", CounterParty = "ML", ExpectedCashPaymentDate = new DateOnly(2025, 1, 10), Source = "Loan Accrual" });
            vm.ExpectedCashFlowItems.Add(new ExpectedCashFlowItemViewModel() { Amount = 1500, Code = "ACC2027", CounterParty = "GS", ExpectedCashPaymentDate = new DateOnly(2025, 1, 10), Source = "Loan Accrual" });
            vm.ExpectedCashFlowItems.Add(new ExpectedCashFlowItemViewModel() { Amount = 4500, Code = "ACC2028", CounterParty = "WAC", ExpectedCashPaymentDate = new DateOnly(2025, 1, 10), Source = "Loan Accrual" });
            this.DataContext = vm;

        }

        private void TableView_DragEnter(object sender, DragEventArgs e)
        {
            var ee = e;
        }

        private void SimpleButton_Click(object sender, RoutedEventArgs e)
        {
            var t = e;
        }
    }
}
