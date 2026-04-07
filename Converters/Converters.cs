using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using SshClient.Services;

namespace SshClient.Converters;

[ValueConversion(typeof(bool), typeof(Visibility))]
public class BoolToVisibleConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c)
        => value is true ? Visibility.Visible : Visibility.Collapsed;
    public object ConvertBack(object value, Type t, object p, CultureInfo c)
        => value is Visibility.Visible;
}

[ValueConversion(typeof(bool), typeof(Visibility))]
public class BoolToCollapsedConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c)
        => value is true ? Visibility.Collapsed : Visibility.Visible;
    public object ConvertBack(object value, Type t, object p, CultureInfo c)
        => value is Visibility.Collapsed;
}

/// <summary>Shows element when string is non-null and non-empty.</summary>
[ValueConversion(typeof(string), typeof(Visibility))]
public class StringToVisibleConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c)
        => string.IsNullOrEmpty(value as string) ? Visibility.Collapsed : Visibility.Visible;
    public object ConvertBack(object value, Type t, object p, CultureInfo c)
        => throw new NotImplementedException();
}

[ValueConversion(typeof(ConnectionState), typeof(Brush))]
public class StateToColorConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c)
    {
        if (value is ConnectionState state)
        {
            return state switch
            {
                ConnectionState.Connected => new SolidColorBrush(Color.FromRgb(63, 185, 80)),
                ConnectionState.Connecting or ConnectionState.Reconnecting =>
                    new SolidColorBrush(Color.FromRgb(210, 153, 34)),
                ConnectionState.Error => new SolidColorBrush(Color.FromRgb(248, 81, 73)),
                _ => new SolidColorBrush(Color.FromRgb(139, 148, 158))
            };
        }
        return Brushes.Gray;
    }
    public object ConvertBack(object value, Type t, object p, CultureInfo c)
        => throw new NotImplementedException();
}

[ValueConversion(typeof(ConnectionState), typeof(string))]
public class StateToLabelConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c)
    {
        return value is ConnectionState s ? s switch
        {
            ConnectionState.Connected => "●  Connected",
            ConnectionState.Connecting => "◌  Connecting…",
            ConnectionState.Reconnecting => "◌  Reconnecting…",
            ConnectionState.Error => "✕  Error",
            _ => "○  Disconnected"
        } : "○  Disconnected";
    }
    public object ConvertBack(object value, Type t, object p, CultureInfo c)
        => throw new NotImplementedException();
}
