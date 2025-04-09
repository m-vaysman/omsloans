using DevExpress.Xpf.Grid;
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
    /// Interaction logic for AccrualEntriesView.xaml
    /// </summary>
    public partial class AccrualEntriesView : UserControl
    {
        public AccrualEntriesView()
        {
            InitializeComponent();
          //  this.DataContext = App.Current.Services.GetService(typeof(AccrualEntriesViewModel));
        }

        private void TableView_Loaded(object sender, RoutedEventArgs e)
        {
         var tbview=   this.Accrual_Grid.View as TableView;
            tbview.BestFitColumns();
        }
    }
}
