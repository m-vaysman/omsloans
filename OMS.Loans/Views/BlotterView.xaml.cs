using DevExpress.Data.Utils;
using OMS.Loans.ViewModels;
using System;
using System.Windows.Controls;

namespace OMS.Loans.Views
{
    /// <summary>
    /// Interaction logic for BlotterView.xaml
    /// </summary>
    public partial class BlotterView : UserControl
    {
        public BlotterView()
        {
            InitializeComponent();
            this.DataContext = App.Current.Services.GetService<BlotterViewModel>();
        }
    }
}
