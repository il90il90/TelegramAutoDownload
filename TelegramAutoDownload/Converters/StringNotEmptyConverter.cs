using System;
using System.Globalization;
using System.Windows.Data;

namespace TelegramAutoDownload.Converters
{
    /// <summary>Returns true when the bound string is non-null and non-whitespace, false otherwise.</summary>
    [ValueConversion(typeof(string), typeof(bool))]
    public class StringNotEmptyConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is string s && !string.IsNullOrWhiteSpace(s);

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
