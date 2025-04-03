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
            serviceCollection.AddDbContext<LoanDbContext>(ServiceLifetime.Scoped);
            serviceCollection.AddTransient<BlotterViewModel>();

            serviceCollection.AddTransient<BlotterEntriesViewModel>();

            return serviceCollection.BuildServiceProvider();
        }
        static App()
        {

            CompatibilitySettings.UseLightweightThemes = true;

            ApplicationThemeHelper.ApplicationThemeName = LightweightTheme.Win11Dark.Name;
            ApplicationThemeHelper.UpdateApplicationThemeName();
            SplashScreenManager.CreateThemed().ShowOnStartup();



        }


        public App()
        {
            Services = ConfigureServices();
        }
    }
}
