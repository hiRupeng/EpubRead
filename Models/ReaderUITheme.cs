namespace EpubRead.Models;

/// <summary>
/// 阅读器界面主题配色（与阅读区主题同步）。
/// 包含工具栏、面板、文字、交互态等界面控件使用的颜色。
/// </summary>
public record ReaderUITheme
{
    /// <summary>整页背景（卡片外的"桌面"色，略深于阅读区以营造浮起层次）</summary>
    public string PageBackground { get; init; } = "#161616";

    /// <summary>阅读区卡片背景（与 WebView2 内 HTML body 背景色一致）</summary>
    public string ReadingBackground { get; init; } = "#202020";

    /// <summary>顶部/底部工具栏背景</summary>
    public string ToolbarBackground { get; init; } = "#2C2C2C";

    /// <summary>侧边面板（目录/设置）背景</summary>
    public string PanelBackground { get; init; } = "#252525";

    /// <summary>面板内更深区域（进度胶囊、快捷键框）背景</summary>
    public string PanelDeepBackground { get; init; } = "#2C2C2C";

    /// <summary>分割线/边框色</summary>
    public string DividerColor { get; init; } = "#3D3D3D";

    /// <summary>主文字色（标题、强调值）</summary>
    public string PrimaryText { get; init; } = "#FFFFFF";

    /// <summary>次要文字色（按钮、章节标题）</summary>
    public string SecondaryText { get; init; } = "#CCCCCC";

    /// <summary>弱化文字色（提示、说明）</summary>
    public string TertiaryText { get; init; } = "#9A9A9A";

    /// <summary>强调主色（选中态背景、进度）</summary>
    public string AccentColor { get; init; } = "#0078D4";

    /// <summary>强调亮色（选中边框、指示条）</summary>
    public string AccentLightColor { get; init; } = "#60CDFF";

    /// <summary>按钮悬浮背景</summary>
    public string HoverBackground { get; init; } = "#383838";

    /// <summary>按钮按下背景</summary>
    public string PressedBackground { get; init; } = "#404040";

    /// <summary>选项按钮默认背景</summary>
    public string OptionBackground { get; init; } = "#3D3D3D";

    /// <summary>选项按钮悬浮背景</summary>
    public string OptionHoverBackground { get; init; } = "#505050";

    /// <summary>滚动条拇指色</summary>
    public string ScrollBarThumb { get; init; } = "#505050";

    /// <summary>构建指定主题的界面配色</summary>
    public static ReaderUITheme Build(ThemeType theme) => theme switch
    {
        ThemeType.Day => new ReaderUITheme
        {
            PageBackground = "#E0E0E0",
            ReadingBackground = "#FAFAFA",
            ToolbarBackground = "#F3F3F3",
            PanelBackground = "#FAFAFA",
            PanelDeepBackground = "#EFEFEF",
            DividerColor = "#E0E0E0",
            PrimaryText = "#1F1F1F",
            SecondaryText = "#5A5A5A",
            TertiaryText = "#8A8A8A",
            AccentColor = "#0078D4",
            AccentLightColor = "#3FA6FF",
            HoverBackground = "#E5E5E5",
            PressedBackground = "#D5D5D5",
            OptionBackground = "#ECECEC",
            OptionHoverBackground = "#DEDEDE",
            ScrollBarThumb = "#B0B0B0"
        },
        ThemeType.Night => new ReaderUITheme
        {
            PageBackground = "#161616",
            ReadingBackground = "#202020",
            ToolbarBackground = "#2C2C2C",
            PanelBackground = "#252525",
            PanelDeepBackground = "#2C2C2C",
            DividerColor = "#3D3D3D",
            PrimaryText = "#FFFFFF",
            SecondaryText = "#CCCCCC",
            TertiaryText = "#9A9A9A",
            AccentColor = "#0078D4",
            AccentLightColor = "#60CDFF",
            HoverBackground = "#383838",
            PressedBackground = "#404040",
            OptionBackground = "#3D3D3D",
            OptionHoverBackground = "#505050",
            ScrollBarThumb = "#505050"
        },
        ThemeType.EyeCare => new ReaderUITheme
        {
            PageBackground = "#C9BB8E",
            ReadingBackground = "#F5E6C8",
            ToolbarBackground = "#ECDCBA",
            PanelBackground = "#F5E6C8",
            PanelDeepBackground = "#EAD9B6",
            DividerColor = "#D2C098",
            PrimaryText = "#2D2519",
            SecondaryText = "#5A4F38",
            TertiaryText = "#8B7D5E",
            AccentColor = "#8B6914",
            AccentLightColor = "#B8901C",
            HoverBackground = "#E2D2AC",
            PressedBackground = "#D6C598",
            OptionBackground = "#EAD9B6",
            OptionHoverBackground = "#E2D2AC",
            ScrollBarThumb = "#A89060"
        },
        _ => Build(ThemeType.Night)
    };
}
