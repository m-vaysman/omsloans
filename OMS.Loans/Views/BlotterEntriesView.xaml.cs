using DevExpress.Data.Utils;
using Microsoft.Extensions.DependencyInjection;
using OMS.Loans.ViewModels;
using System;
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
            ScreenScopeLifetime.HoldUntilWindowCloses(this, screenScope, DataContext);
        }
    }
}
