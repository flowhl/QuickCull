using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Data;

namespace ImageCullingTool.WPF.Converters
{
    public class PickIndicatorConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            var pick = values[0] as bool?;

            return pick switch
            {
                true => "✓",      // Green checkmark for pick
                false => "✗",     // Red X for reject
                null => ""        // Nothing for unset
            };
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
