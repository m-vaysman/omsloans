using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Data;
using System.Windows;
using DevExpress.Xpf.Editors;

namespace OMS.Loans.Infrastructure
{
    public static class FocusHelper
    {
        public static bool GetUpdateBindingOnLostFocus(DependencyObject obj)
        {
            return (bool)obj.GetValue(UpdateBindingOnLostFocusProperty);
        }

        public static void SetUpdateBindingOnLostFocus(DependencyObject obj, bool value)
        {
            obj.SetValue(UpdateBindingOnLostFocusProperty, value);
        }

        public static readonly DependencyProperty UpdateBindingOnLostFocusProperty =
            DependencyProperty.RegisterAttached(
                "UpdateBindingOnLostFocus",
                typeof(bool),
                typeof(FocusHelper),
                new UIPropertyMetadata(false, OnUpdateBindingOnLostFocusChanged));

        private static void OnUpdateBindingOnLostFocusChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is UIElement element)
            {
                if ((bool)e.NewValue)
                {
                    element.LostFocus += Element_LostFocus;
                }
                else
                {
                    element.LostFocus -= Element_LostFocus;
                }
            }
        }

        private static void Element_LostFocus(object sender, RoutedEventArgs e)
        {
            var binding = BindingOperations.GetBindingExpression((DependencyObject)sender, TextEdit.TextProperty);
            binding?.UpdateSource(); // Forces validation regardless of value change
        }
    }
}
