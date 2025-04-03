using DevExpress.Data.Utils;
using OMS.Loans.ViewModels;
using System;
using System.Windows;
using System.Windows.Controls;

namespace OMS.Loans.Views
{
    /// <summary>
    /// Interaction logic for BlotterEntriesView.xaml
    /// </summary>
    public partial class BlotterEntriesView : UserControl
    {
        public BlotterEntriesView()
        {
            InitializeComponent();
            this.DataContext = App.Current.Services.GetService<BlotterEntriesViewModel>();
        }

        private void GridControl_LostFocus(object sender, RoutedEventArgs e)
        {
            var gridControl = sender as DevExpress.Xpf.Grid.GridControl;
            if (gridControl != null)
            {
                gridControl.SelectedItems.Clear();
            }
        }
    }
}
