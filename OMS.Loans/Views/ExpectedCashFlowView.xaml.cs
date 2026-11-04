using Microsoft.Extensions.DependencyInjection;
using OMS.Loans.ViewModels;
using System.Windows.Controls;

namespace OMS.Loans.Views
{
    public partial class ExpectedCashFlowView : UserControl
    {
        private readonly IServiceScope screenScope;

        public ExpectedCashFlowView()
        {
            InitializeComponent();
            screenScope = App.Current.CreateScreenScope();
            DataContext = screenScope.ServiceProvider.GetRequiredService<ExpectedCashFlowViewModel>();
            ScreenScopeLifetime.HoldUntilWindowCloses(this, screenScope, DataContext);
        }
    }
}
