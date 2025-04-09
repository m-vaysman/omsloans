using DevExpress.Mvvm.POCO;
using DevExpress.Xpf.Editors;
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
    /// Interaction logic for TestView.xaml
    /// </summary>
    public partial class TestView : UserControl
    {
        public TestView()
        {
            InitializeComponent();
            this.DataContext = App.Current.Services.GetService(typeof(TradeEntryViewModel));
        }

        private async void ReInitializeViewModel_RequestNavigation(object sender, HyperlinkEditRequestNavigationEventArgs e)
        {
            var vm = App.Current.Services.GetService(typeof(TradeEntryViewModel)) as TradeEntryViewModel;
           await vm.InitializeViewModel();
        }
    }
}
