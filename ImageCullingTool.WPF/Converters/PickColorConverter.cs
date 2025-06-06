using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Data;
using System.Windows.Media;

namespace ImageCullingTool.WPF.Converters
{

    public class PickColorConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            var pick = values[0] as bool?;

            return pick switch
            {
                true => new SolidColorBrush(Colors.Green),
                false => new SolidColorBrush(Colors.Red),
                null => new SolidColorBrush(Colors.Gray)
            };
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
