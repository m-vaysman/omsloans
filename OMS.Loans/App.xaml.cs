using AutoMapper;
using DevExpress.Xpf.Core;
using LoanDbModel;
using Microsoft.Extensions.DependencyInjection;
using OMS.Loans.Common;
using OMS.Loans.Mapping;
using OMS.Loans.Services;
using OMS.Loans.ViewModels;
using System;
using System.Windows;

namespace OMS.Loans
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        public new static App Current => (App)Application.Current;
        public IServiceProvider Services { get; }
        private static IServiceProvider ConfigureServices()
        {

            var serviceCollection = new ServiceCollection();

            serviceCollection.AddSingleton<IMapper>(mapper => new MappingProfile().Mapper);
            serviceCollection.AddScoped<ICounterParties, CounterPartyService>();
            serviceCollection.AddScoped<IBlotterEntries, BlotterEntriesService>();
            serviceCollection.AddScoped<ITradeEntries, TradeEntriesService>();
            serviceCollection.AddScoped<ITradeDocumentRetriever, DocumentsRetrievingService>();
            serviceCollection.AddDbContext<LoanDbContext>(ServiceLifetime.Scoped);
            serviceCollection.AddTransient<BlotterViewModel>();
            serviceCollection.AddScoped<IAccrualEntries, AccrualsEntriesService>();
            serviceCollection.AddScoped<ITradeBalanceVsAccrued, TradeBalanceVsAccruedService>();
            serviceCollection.AddTransient<AccrualEntriesViewModel>();
            serviceCollection.AddTransient<BlotterEntriesViewModel>();
            serviceCollection.AddTransient<TradeEntryViewModel>();
            return serviceCollection.BuildServiceProvider();
        }
        static App()
        {

            CompatibilitySettings.UseLightweightThemes = true;
            //LightweightTheme.Office2019BlackBrickwork.Name;
            ApplicationThemeHelper.ApplicationThemeName = LightweightTheme.Office2019BlackCobaltBlue.Name;
            ApplicationThemeHelper.UpdateApplicationThemeName();
            SplashScreenManager.CreateThemed().ShowOnStartup();
       

        }


        public App()
        {
            Services = ConfigureServices();
        }
    }
}
