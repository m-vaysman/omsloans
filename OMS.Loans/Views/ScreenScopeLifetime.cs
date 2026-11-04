using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Windows;

namespace OMS.Loans.Views
{
    /// <summary>
    /// Keeps a screen scope alive across DevExpress dock reparenting.
    /// Unloaded fires for auto-hide, float, and re-dock, then Loaded fires again
    /// on the same view. The scope is disposed when the window that hosts the view closes.
    /// </summary>
    static class ScreenScopeLifetime
    {
        public static void HoldUntilWindowCloses(FrameworkElement view, IServiceScope scope, object recipient)
        {
            Window hooked = null;

            void CloseScope(object sender, EventArgs e)
            {
                if (hooked != null)
                {
                    hooked.Closed -= CloseScope;
                    hooked = null;
                }

                if (recipient != null)
                {
                    WeakReferenceMessenger.Default.UnregisterAll(recipient);
                }

                scope.Dispose();
            }

            void Attach(object sender, RoutedEventArgs e)
            {
                var window = Window.GetWindow(view);
                if (window == null || ReferenceEquals(window, hooked))
                {
                    return;
                }

                if (hooked != null)
                {
                    hooked.Closed -= CloseScope;
                }

                hooked = window;
                window.Closed += CloseScope;
            }

            view.Loaded += Attach;
        }
    }
}
