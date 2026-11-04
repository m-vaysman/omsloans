using Microsoft.Extensions.DependencyInjection;
using OMS.Loans.ViewModels;
using System.Windows.Controls;

namespace OMS.Loans.Views
{
    public partial class CashMatchingView : UserControl
    {
        private readonly IServiceScope screenScope;

        public CashMatchingView()
        {
            InitializeComponent();
            screenScope = App.Current.CreateScreenScope();
            DataContext = screenScope.ServiceProvider.GetRequiredService<ExpectedCashFlowItemMergedViewModel>();
            Unloaded += (_, _) => screenScope.Dispose();
        }
    }
}
