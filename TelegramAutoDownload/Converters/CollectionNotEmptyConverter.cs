using System;
using System.Collections;
using System.Globalization;
using System.Windows.Data;

namespace TelegramAutoDownload.Converters
{
    /// <summary>Returns true when the bound ICollection has at least one element.</summary>
    [ValueConversion(typeof(ICollection), typeof(bool))]
    public class CollectionNotEmptyConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is ICollection col && col.Count > 0;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
