using DevExpress.Data.Utils;
using Microsoft.Extensions.DependencyInjection;
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
        private readonly IServiceScope screenScope;

        public BlotterEntriesView()
        {
            InitializeComponent();
            screenScope = App.Current.CreateScreenScope();
            DataContext = screenScope.ServiceProvider.GetRequiredService<BlotterEntriesViewModel>();
            Unloaded += (_, _) => screenScope.Dispose();
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
