using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Data;

namespace ImageCullingTool.WPF.Converters
{
    public class RatingStarsConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            var lrRating = values[0] as int?;
            var aiRating = values[1] as int?;

            var rating = lrRating ?? aiRating;

            if (!rating.HasValue)
                return "";

            // Convert to star symbols
            var stars = "";
            for (int i = 1; i <= 5; i++)
            {
                stars += i <= rating ? "★" : "☆";
            }

            return stars;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
