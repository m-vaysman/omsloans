using DevExpress.Data.Utils;
using Microsoft.Extensions.DependencyInjection;
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
        private readonly IServiceScope screenScope;

        public BlotterView()
        {
            InitializeComponent();
            screenScope = App.Current.CreateScreenScope();
            DataContext = screenScope.ServiceProvider.GetRequiredService<BlotterViewModel>();
            Unloaded += (_, _) => screenScope.Dispose();
        }
    }
}
