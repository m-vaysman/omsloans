using Microsoft.Extensions.DependencyInjection;
using OMS.Loans.ViewModels;
using System.Windows.Controls;

namespace OMS.Loans.Views
{
    public partial class ExternalCashFlowView : UserControl
    {
        private readonly IServiceScope screenScope;

        public ExternalCashFlowView()
        {
            InitializeComponent();
            screenScope = App.Current.CreateScreenScope();
            DataContext = screenScope.ServiceProvider.GetRequiredService<ExternalCashFlowViewModel>();
            ScreenScopeLifetime.HoldUntilWindowCloses(this, screenScope, DataContext);
        }
    }
}
