using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Data;
using System.Windows.Media;

namespace QuickCull.WPF.Converters
{
    public class StatusColorConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            var analysisDate = values[0] as DateTime?;
            var rating = values[1] as int?;

            if (!analysisDate.HasValue)
                return new SolidColorBrush(Colors.Gray); // Not analyzed

            if (rating >= 4)
                return new SolidColorBrush(Colors.Green); // High quality
            else if (rating >= 3)
                return new SolidColorBrush(Colors.Orange); // Medium quality
            else
                return new SolidColorBrush(Colors.Red); // Low quality
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
