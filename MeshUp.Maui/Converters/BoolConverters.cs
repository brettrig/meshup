using System.Globalization;

namespace MeshUp.Maui.Converters;

/// <summary>Aligns "my" messages to the right (like WhatsApp) and others' to the left.</summary>
public class BoolToAlignmentConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? LayoutOptions.End : LayoutOptions.Start;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Gives "my" message bubbles a distinct color from received messages.</summary>
public class BoolToBubbleColorConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? Color.FromArgb("#DCF8C6") : Color.FromArgb("#FFFFFF");

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
