using System.Globalization;
using System.IO;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace EpubRead.Converters;

/// <summary>
/// 封面路径 → ImageSource 转换器，缺失时返回默认占位图
/// </summary>
public class CoverPathConverter : IValueConverter
{
    private static readonly SolidColorBrush PlaceholderBrush = new(Color.FromRgb(0x3A, 0x3A, 0x4A));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var path = value as string;
        if (!string.IsNullOrEmpty(path) && File.Exists(path))
        {
            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.UriSource = new Uri(path, UriKind.Absolute);
                bitmap.DecodePixelWidth = 360;
                bitmap.EndInit();
                bitmap.Freeze();
                return bitmap;
            }
            catch
            {
                // 图片损坏，回退占位图
            }
        }

        // 返回纯色占位画刷
        return PlaceholderBrush;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

/// <summary>
/// null/空字符串 → Visibility 转换器，用于空状态提示
/// </summary>
public class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool invert = parameter as string == "invert";
        bool isEmpty = value == null || (value is int count && count == 0);

        return invert
            ? (isEmpty ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed)
            : (isEmpty ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

/// <summary>
/// 阅读进度百分比 (0-100) → Visibility 转换器，进度为 0 时隐藏
/// </summary>
public class ProgressToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is double percent && percent > 0)
            return System.Windows.Visibility.Visible;
        return System.Windows.Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
