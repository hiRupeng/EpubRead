using System.Globalization;
using System.Windows.Data;
using EpubRead.Models;

namespace EpubRead.Converters;

/// <summary>
/// 章节 Level 到左边距的转换器（每级缩进 20px）
/// </summary>
public class LevelToMarginConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is int level)
            return new System.Windows.Thickness(level * 20 + 4, 0, 4, 0);
        return new System.Windows.Thickness(4, 0, 4, 0);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// 章节 Level 到图标/指示符的转换器
/// </summary>
public class LevelToIconConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is int level)
        {
            return level switch
            {
                0 => "📁",
                1 => "📄",
                _ => "·"
            };
        }
        return "·";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// 通用枚举逐个比较转换器（用于 RadioButton IsChecked 绑定）
/// 将 ViewModel 的枚举属性值与 CommandParameter 比较
/// </summary>
public class EnumEqualsConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value == null || parameter == null) return false;

        var paramStr = parameter.ToString();
        var valueStr = value.ToString();

        return string.Equals(paramStr, valueStr, StringComparison.OrdinalIgnoreCase);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Binding.DoNothing;
}

/// <summary>
/// ThemeType 比较转换器
/// </summary>
public class ThemeEqualsConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value == null || parameter == null) return false;

        var paramStr = parameter.ToString();
        var valueStr = value.ToString();

        return string.Equals(paramStr, valueStr, StringComparison.OrdinalIgnoreCase);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Binding.DoNothing;
}

/// <summary>
/// 0-1 进度值转换为进度条宽度（使用父容器宽度的百分比）
/// </summary>
public class PercentToWidthConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is double percent && percent > 0)
            return new System.Windows.GridLength(percent, System.Windows.GridUnitType.Star);
        return new System.Windows.GridLength(0);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// 页宽选项转换为阅读卡片最大宽度（px），使 WebView2 卡片如 A4 纸般收窄居中悬浮。
/// 与 HTML body 的 max-width 保持一致，卡片宽度即内容宽度。
/// </summary>
public class PageWidthToMaxWidthConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is PageWidthOption option)
        {
            return option switch
            {
                PageWidthOption.Narrow => 680.0,
                PageWidthOption.Medium => 820.0,
                PageWidthOption.Wide => 960.0,
                _ => 820.0
            };
        }
        return 820.0;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
