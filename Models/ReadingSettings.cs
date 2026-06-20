namespace EpubRead.Models;

/// <summary>
/// 阅读主题类型
/// </summary>
public enum ThemeType
{
    /// <summary>白天模式：米黄底色 + 深色文字</summary>
    Day,

    /// <summary>夜晚模式：深色底色 + 柔白文字</summary>
    Night,

    /// <summary>护眼模式：浅绿底色 + 深灰文字</summary>
    EyeCare
}

/// <summary>
/// 字体选择
/// </summary>
public enum FontFamilyOption
{
    /// <summary>衬线字体（宋体 / Georgia）</summary>
    Serif,

    /// <summary>无衬线字体（微软雅黑 / Arial）</summary>
    SansSerif,

    /// <summary>等宽字体（Courier New）</summary>
    Monospace
}

/// <summary>
/// 行间距选项
/// </summary>
public enum LineHeightOption
{
    /// <summary>紧凑 1.4</summary>
    Compact,

    /// <summary>标准 1.85</summary>
    Normal,

    /// <summary>宽松 2.2</summary>
    Loose
}

/// <summary>
/// 页宽选项
/// </summary>
public enum PageWidthOption
{
    /// <summary>窄 680px</summary>
    Narrow,

    /// <summary>中 820px</summary>
    Medium,

    /// <summary>宽 960px</summary>
    Wide
}

/// <summary>
/// 阅读设置数据实体，存储用户的阅读偏好
/// </summary>
public class ReadingSettings
{
    /// <summary>唯一标识（单行表，固定值 "default"）</summary>
    public string Id { get; set; } = "default";

    /// <summary>阅读主题</summary>
    public ThemeType Theme { get; set; } = ThemeType.Day;

    /// <summary>字体大小（px）</summary>
    public int FontSize { get; set; } = 17;

    /// <summary>字体选择</summary>
    public FontFamilyOption FontFamily { get; set; } = FontFamilyOption.Serif;

    /// <summary>行间距</summary>
    public LineHeightOption LineHeight { get; set; } = LineHeightOption.Normal;

    /// <summary>页宽</summary>
    public PageWidthOption PageWidth { get; set; } = PageWidthOption.Medium;

    /// <summary>导航模式</summary>
    public NavigationMode NavigationMode { get; set; } = Models.NavigationMode.BySection;
}
