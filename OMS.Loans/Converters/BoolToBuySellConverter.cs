using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Data;

namespace OMS.Loans.Converters
{
 

    public class BoolToBuySellConverter : IValueConverter
    {
  
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool b)
                return b ? "B" : "S";

            return "S"; // default to "S" if not a bool
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string s)
                return s == "S" ? false : true;

            return false;
        }

    }

}
