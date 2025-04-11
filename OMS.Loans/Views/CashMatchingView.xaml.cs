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
    /// Interaction logic for CashMatchingView.xaml
    /// </summary>
    public partial class CashMatchingView : UserControl
    {
     
        public CashMatchingView()
        {
            InitializeComponent();
            this.DataContext = new ExpectedCashFlowItemMergedViewModel();
    
        }

        private void ListBoxEdit_DragEnter(object sender, DragEventArgs e)
        {
            var ee = e;
        }

        private void ListBoxEdit_Drop(object sender, DragEventArgs e)
        {
            var ee = e;
        }
    }
}
