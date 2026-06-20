using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EpubRead.Models;
using EpubRead.Services;

namespace EpubRead.ViewModels;

public partial class ReaderViewModel : ObservableObject
{
    private readonly EpubParser _epubParser;
    private readonly ReadingSettingsService _settingsService;
    private EpubBook? _epubBook;
    private string _filePath = string.Empty;

    // ─── 章节数据 ───

    /// <summary>
    /// 层级章节树（用于目录面板展示）
    /// </summary>
    public ObservableCollection<Chapter> TocChapters { get; } = [];

    /// <summary>
    /// 展平的阅读顺序列表（用于导航）
    /// </summary>
    public ObservableCollection<Chapter> FlatChapters { get; } = [];

    [ObservableProperty]
    private Chapter? _currentChapter;

    [ObservableProperty]
    private string _bookTitle = string.Empty;

    [ObservableProperty]
    private string _progressText = string.Empty;

    // ─── 面板状态 ───

    [ObservableProperty]
    private bool _isTocOpen;

    [ObservableProperty]
    private bool _isSettingsOpen;

    // ─── 导航状态 ───

    [ObservableProperty]
    private bool _hasPrev;

    [ObservableProperty]
    private bool _hasNext;

    [ObservableProperty]
    private NavigationMode _navigationMode = Models.NavigationMode.BySection;

    // ─── 阅读设置 ───

    [ObservableProperty]
    private ThemeType _selectedTheme = ThemeType.Day;

    [ObservableProperty]
    private int _fontSize = 17;

    [ObservableProperty]
    private FontFamilyOption _selectedFontFamily = FontFamilyOption.Serif;

    [ObservableProperty]
    private LineHeightOption _selectedLineHeight = LineHeightOption.Normal;

    [ObservableProperty]
    private PageWidthOption _selectedPageWidth = PageWidthOption.Medium;

    /// <summary>顶部进度（0~1）</summary>
    [ObservableProperty]
    private double _topProgress;

    /// <summary>当前进度百分比文本</summary>
    [ObservableProperty]
    private string _progressPercentText = string.Empty;

    // ─── 事件 ───

    public event EventHandler<string>? ContentRequested;
    public event EventHandler<string?>? ScrollToAnchorRequested;
    public event EventHandler? GoBackRequested;
    public event EventHandler<(int chapterIndex, int totalChapters)>? ProgressChanged;
    /// <summary>阅读设置变化时触发，传递要注入的 JavaScript 代码</summary>
    public event EventHandler<string>? StyleInjectionRequested;

    public ReaderViewModel(EpubParser epubParser, ReadingSettingsService settingsService)
    {
        _epubParser = epubParser;
        _settingsService = settingsService;
    }

    // ─── 书籍加载 ───

    /// <summary>
    /// 加载 EPUB 书籍并导航到上次阅读位置或第一章
    /// </summary>
    public void LoadBook(Book book)
    {
        _filePath = book.FilePath;
        BookTitle = book.Title;

        // 先加载阅读设置
        var savedSettings = _settingsService.LoadSettings();
        ApplySettings(savedSettings);

        _epubBook = _epubParser.Parse(_filePath);

        TocChapters.Clear();
        FlatChapters.Clear();

        foreach (var ch in _epubBook.Chapters)
            TocChapters.Add(ch);
        foreach (var ch in _epubBook.FlatChapters)
            FlatChapters.Add(ch);

        if (FlatChapters.Count == 0) return;

        // 恢复上次阅读位置
        int startIndex = book.LastReadChapterIndex;
        if (startIndex >= 0 && startIndex < FlatChapters.Count)
            NavigateToChapter(FlatChapters[startIndex]);
        else
            NavigateToChapter(FlatChapters[0]);
    }

    private void ApplySettings(ReadingSettings s)
    {
        SelectedTheme = s.Theme;
        FontSize = s.FontSize;
        SelectedFontFamily = s.FontFamily;
        SelectedLineHeight = s.LineHeight;
        SelectedPageWidth = s.PageWidth;
        NavigationMode = s.NavigationMode;
    }

    private ReadingSettings CollectSettings() => new()
    {
        Theme = SelectedTheme,
        FontSize = FontSize,
        FontFamily = SelectedFontFamily,
        LineHeight = SelectedLineHeight,
        PageWidth = SelectedPageWidth,
        NavigationMode = NavigationMode
    };

    private void SaveCurrentSettings()
    {
        _settingsService.SaveSettings(CollectSettings());
    }

    // ─── 面板切换 ───

    [RelayCommand]
    private void ToggleToc()
    {
        IsTocOpen = !IsTocOpen;
        if (IsTocOpen) IsSettingsOpen = false;
    }

    [RelayCommand]
    private void ToggleSettings()
    {
        IsSettingsOpen = !IsSettingsOpen;
        if (IsSettingsOpen) IsTocOpen = false;
    }

    // ─── 章节导航 ───

    [RelayCommand]
    private void NavigateToChapter(Chapter? chapter)
    {
        if (chapter == null || _epubBook == null) return;

        chapter.Content = _epubParser.LoadChapterContent(
            _filePath, _epubBook.BasePath, chapter.Href);

        CurrentChapter = chapter;

        var styledContent = GenerateReadingStyle(chapter.Content ?? "<p>内容为空</p>", chapter.Href);
        ContentRequested?.Invoke(this, styledContent);

        if (!string.IsNullOrEmpty(chapter.Anchor))
        {
            ScrollToAnchorRequested?.Invoke(this, chapter.Anchor);
        }

        UpdateNavigation();

        var idx = FlatChapters.IndexOf(chapter);
        if (idx >= 0)
            ProgressChanged?.Invoke(this, (idx, FlatChapters.Count));
    }

    /// <summary>
    /// 根据 href 查找并导航（供 WebView2 超链接拦截使用）
    /// </summary>
    public void NavigateToHref(string href)
    {
        if (_epubBook == null) return;

        // 当前页内锚点（#xxx）
        if (href.StartsWith("#"))
        {
            var anchor = href[1..];
            if (!string.IsNullOrEmpty(anchor))
                ScrollToAnchorRequested?.Invoke(this, anchor);
            return;
        }

        // 分离锚点
        var hashIdx = href.IndexOf('#');
        var hrefPart = hashIdx >= 0 ? href[..hashIdx] : href;
        var anchorPart = hashIdx >= 0 ? href[(hashIdx + 1)..] : null;

        // 候选路径：原始 href，以及相对于当前章节文件所在目录解析后的路径
        var candidates = BuildHrefCandidates(hrefPart);

        // 1) 在扁平章节列表中查找匹配
        foreach (var candidate in candidates)
        {
            var target = EpubParser.ResolveHref(candidate, _epubBook.BasePath, _epubBook.FlatChapters);
            if (target != null)
            {
                NavigateToChapter(target);
                // 链接自带锚点且与目标章节自身锚点不同时，滚动到链接指定位置
                if (!string.IsNullOrEmpty(anchorPart) && anchorPart != target.Anchor)
                    ScrollToAnchorRequested?.Invoke(this, anchorPart);
                return;
            }
        }

        // 2) 兜底：直接按路径加载章节内容
        foreach (var candidate in candidates)
        {
            var simpleHref = candidate;
            if (simpleHref.StartsWith(_epubBook.BasePath, StringComparison.OrdinalIgnoreCase))
                simpleHref = simpleHref[_epubBook.BasePath.Length..];

            var content = _epubParser.LoadChapterContent(_filePath, _epubBook.BasePath, simpleHref);
            if (!string.IsNullOrEmpty(content) &&
                !content.Equals("<p>内容加载失败</p>", StringComparison.Ordinal))
            {
                var styled = GenerateReadingStyle(content, simpleHref);
                ContentRequested?.Invoke(this, styled);
                if (!string.IsNullOrEmpty(anchorPart))
                    ScrollToAnchorRequested?.Invoke(this, anchorPart);
                return;
            }
        }

        // 全部失败：给出提示
        ContentRequested?.Invoke(this, GenerateReadingStyle("<p style='color:#888;text-align:center;margin-top:60px;'>无法跳转到该链接</p>"));
    }

    /// <summary>
    /// 构造链接解析候选路径：原始路径 + 相对于当前章节文件所在目录解析后的路径
    /// </summary>
    private List<string> BuildHrefCandidates(string hrefPart)
    {
        var candidates = new List<string> { hrefPart };

        if (CurrentChapter != null && !string.IsNullOrEmpty(CurrentChapter.Href))
        {
            var currentDir = CurrentChapter.Href;
            var lastSlash = currentDir.LastIndexOf('/');
            if (lastSlash >= 0)
            {
                currentDir = currentDir[..(lastSlash + 1)];
                var combined = EpubParser.NormalizePath(currentDir + hrefPart);
                if (!candidates.Contains(combined, StringComparer.OrdinalIgnoreCase))
                    candidates.Add(combined);
            }
        }

        return candidates;
    }

    [RelayCommand]
    private void NavigatePrev()
    {
        if (CurrentChapter == null) return;

        if (NavigationMode == Models.NavigationMode.BySection)
        {
            var idx = FlatChapters.IndexOf(CurrentChapter);
            if (idx > 0)
                NavigateToChapter(FlatChapters[idx - 1]);
        }
        else
        {
            NavigateToPrevSpineRoot();
        }
    }

    [RelayCommand]
    private void NavigateNext()
    {
        if (CurrentChapter == null) return;

        if (NavigationMode == Models.NavigationMode.BySection)
        {
            var idx = FlatChapters.IndexOf(CurrentChapter);
            if (idx < FlatChapters.Count - 1)
                NavigateToChapter(FlatChapters[idx + 1]);
        }
        else
        {
            NavigateToNextSpineRoot();
        }
    }

    private void NavigateToPrevSpineRoot()
    {
        if (CurrentChapter == null) return;
        var currentGroup = GetCurrentSpineGroup();
        if (currentGroup.Count == 0) return;

        var firstInGroup = FlatChapters.IndexOf(currentGroup[0]);
        if (firstInGroup <= 0) return;

        for (int i = firstInGroup - 1; i >= 0; i--)
        {
            if (FlatChapters[i].IsSpineRoot)
            {
                NavigateToChapter(FlatChapters[i]);
                return;
            }
        }
        if (firstInGroup > 0)
            NavigateToChapter(FlatChapters[firstInGroup - 1]);
    }

    private void NavigateToNextSpineRoot()
    {
        if (CurrentChapter == null) return;
        var currentGroup = GetCurrentSpineGroup();
        if (currentGroup.Count == 0) return;

        var lastInGroup = FlatChapters.IndexOf(currentGroup[^1]);
        if (lastInGroup < 0 || lastInGroup >= FlatChapters.Count - 1) return;

        for (int i = lastInGroup + 1; i < FlatChapters.Count; i++)
        {
            if (FlatChapters[i].IsSpineRoot)
            {
                NavigateToChapter(FlatChapters[i]);
                return;
            }
        }
    }

    private List<Chapter> GetCurrentSpineGroup()
    {
        if (CurrentChapter == null || string.IsNullOrEmpty(CurrentChapter.Href))
            return [];

        return FlatChapters.Where(c =>
            string.Equals(c.Href, CurrentChapter.Href, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    [RelayCommand]
    private void GoBack()
    {
        GoBackRequested?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateNavigation()
    {
        if (CurrentChapter == null) return;

        if (NavigationMode == Models.NavigationMode.BySection)
        {
            var idx = FlatChapters.IndexOf(CurrentChapter);
            HasPrev = idx > 0;
            HasNext = idx < FlatChapters.Count - 1;
            ProgressText = $"第 {idx + 1} / {FlatChapters.Count} 章";
            TopProgress = FlatChapters.Count > 1 ? (double)(idx + 1) / FlatChapters.Count : 0;
            ProgressPercentText = $"{(int)(TopProgress * 100)}%";
        }
        else
        {
            var group = GetCurrentSpineGroup();
            var firstIdx = group.Count > 0 ? FlatChapters.IndexOf(group[0]) : -1;
            var totalFiles = FlatChapters.Count(c => c.IsSpineRoot);
            var currentFileIndex = FlatChapters.TakeWhile(c => c != group[0])
                .Count(c => c.IsSpineRoot);

            HasPrev = firstIdx > 0;
            HasNext = firstIdx < FlatChapters.Count - 1;
            ProgressText = $"第 {currentFileIndex + 1} / {totalFiles} 个文件";
            TopProgress = totalFiles > 0 ? (double)(currentFileIndex + 1) / totalFiles : 0;
            ProgressPercentText = $"{(int)(TopProgress * 100)}%";
        }
    }

    // ─── 导航模式切换 ───

    [RelayCommand]
    private void SwitchNavigationMode(NavigationMode mode)
    {
        NavigationMode = mode;
        UpdateNavigation();
        SaveCurrentSettings();
    }

    // ─── 阅读设置命令 ───

    [RelayCommand]
    private void SetTheme(ThemeType theme)
    {
        SelectedTheme = theme;
        ApplyReadingStyle();
        SaveCurrentSettings();
    }

    [RelayCommand]
    private void IncreaseFontSize()
    {
        if (FontSize < 28)
        {
            FontSize += 2;
            ApplyReadingStyle();
            SaveCurrentSettings();
        }
    }

    [RelayCommand]
    private void DecreaseFontSize()
    {
        if (FontSize > 14)
        {
            FontSize -= 2;
            ApplyReadingStyle();
            SaveCurrentSettings();
        }
    }

    [RelayCommand]
    private void SetFontFamily(FontFamilyOption option)
    {
        SelectedFontFamily = option;
        ApplyReadingStyle();
        SaveCurrentSettings();
    }

    [RelayCommand]
    private void SetLineHeight(LineHeightOption option)
    {
        SelectedLineHeight = option;
        ApplyReadingStyle();
        SaveCurrentSettings();
    }

    [RelayCommand]
    private void SetPageWidth(PageWidthOption option)
    {
        SelectedPageWidth = option;
        ApplyReadingStyle();
        SaveCurrentSettings();
    }

    /// <summary>当设置变化时重新生成 CSS 并通过 JavaScript 注入到 WebView2</summary>
    private void ApplyReadingStyle()
    {
        var jsCss = GenerateStyleScript();
        StyleInjectionRequested?.Invoke(this, jsCss);
    }

    // ─── CSS 样式生成 ───

    /// <summary>从 EPUB ZIP 中读取资源（供 ReaderPage 的 WebResourceRequested 调用）</summary>
    public (byte[]? data, string mediaType) LoadResource(string resourcePath)
    {
        if (_epubBook == null || string.IsNullOrEmpty(_filePath))
            return (null, "");
        return _epubParser.LoadResource(_filePath, resourcePath);
    }

    /// <summary>提取文件所在目录（含尾部斜杠），如 "Text/chapter1.xhtml" → "Text/"</summary>
    private static string GetFileDirectory(string href)
    {
        var normalized = href.Replace('\\', '/');
        var lastSlash = normalized.LastIndexOf('/');
        return lastSlash >= 0 ? normalized[..(lastSlash + 1)] : "";
    }

    /// <summary>
    /// 构建 base 标签，使 EPUB 内容中的相对路径（图片、CSS等）能解析到虚拟主机 epub.local，
    /// 由 WebResourceRequested 拦截后从 ZIP 读取。
    /// </summary>
    private string BuildBaseTag(string? fileHref)
    {
        if (_epubBook == null) return "";
        var href = fileHref ?? CurrentChapter?.Href;
        if (string.IsNullOrEmpty(href)) return "";
        var fileDir = GetFileDirectory(href);
        return $"<base href=\"https://epub.local/{_epubBook.BasePath}{fileDir}\">";
    }

    /// <summary>生成完整的阅读页面 HTML（切换章节时使用）</summary>
    private string GenerateReadingStyle(string content, string? fileHref = null)
    {
        var bodyContent = ExtractBodyContent(content);
        var (bg, color, hColor, blockquoteColor) = GetThemeColors();
        var fontFamily = GetFontFamilyCss();
        var lineHeight = GetLineHeightCss();
        var pageWidth = GetPageWidthCss();
        var baseTag = BuildBaseTag(fileHref);

        return $$"""
            <!DOCTYPE html>
            <html>
            <head>
            <meta charset="utf-8">
            {{baseTag}}
            <style id="epub-reader-style">
              * { margin: 0; padding: 0; box-sizing: border-box; }
              body {
                font-family: {{fontFamily}};
                font-size: {{FontSize}}px;
                line-height: {{lineHeight}};
                color: {{color}};
                background: {{bg}};
                padding: 48px 32px;
                max-width: {{pageWidth}};
                margin: 0 auto;
                transition: background 0.3s ease, color 0.3s ease;
              }
              h1, h2, h3 { color: {{hColor}}; margin: 1.2em 0 0.6em; }
              h1 { font-size: 1.6em; }
              h2 { font-size: 1.3em; }
              p { margin: 0.8em 0; text-indent: 2em; }
              img { max-width: 100%; height: auto; display: block; margin: 12px auto; border-radius: 6px; }
              blockquote { border-left: 4px solid {{blockquoteColor}}; padding-left: 16px; margin: 1em 0; color: {{color}}; opacity: 0.8; }
              a { color: {{blockquoteColor}}; }
              ::selection { background: {{blockquoteColor}}; color: #fff; }
              ::-webkit-scrollbar { width: 6px; }
              ::-webkit-scrollbar-thumb { background: rgba(0,0,0,0.2); border-radius: 3px; }
            </style>
            </head>
            <body>
            {{bodyContent}}
            </body>
            </html>
            """;
    }

    /// <summary>
    /// 生成用于注入当前页面的 JavaScript 代码（设置变化时使用，不重载页面）
    /// </summary>
    private string GenerateStyleScript()
    {
        var (bg, color, hColor, blockquoteColor) = GetThemeColors();
        var fontFamily = GetFontFamilyCss();
        var lineHeight = GetLineHeightCss();
        var pageWidth = GetPageWidthCss();

        // 转义引号，防止 JavaScript 字符串问题
        var safeBg = EscapeJsString(bg);
        var safeColor = EscapeJsString(color);
        var safeHColor = EscapeJsString(hColor);
        var safeBqColor = EscapeJsString(blockquoteColor);
        var safeFont = EscapeJsString(fontFamily);
        var safeWidth = EscapeJsString(pageWidth);

        return $@"
(function(){{
    var s = document.getElementById('epub-reader-style');
    if (!s) {{ s = document.createElement('style'); s.id = 'epub-reader-style'; document.head.appendChild(s); }}
    s.textContent = `
      * {{ margin: 0; padding: 0; box-sizing: border-box; }}
      body {{
        font-family: {safeFont};
        font-size: {FontSize}px;
        line-height: {lineHeight};
        color: {safeColor};
        background: {safeBg};
        padding: 48px 32px;
        max-width: {safeWidth};
        margin: 0 auto;
      }}
      h1, h2, h3 {{ color: {safeHColor}; margin: 1.2em 0 0.6em; }}
      h1 {{ font-size: 1.6em; }}
      h2 {{ font-size: 1.3em; }}
      p {{ margin: 0.8em 0; text-indent: 2em; }}
      img {{ max-width: 100%; height: auto; display: block; margin: 12px auto; border-radius: 6px; }}
      blockquote {{ border-left: 4px solid {safeBqColor}; padding-left: 16px; margin: 1em 0; color: {safeColor}; opacity: 0.8; }}
      a {{ color: {safeBqColor}; }}
      ::selection {{ background: {safeBqColor}; color: #fff; }}
      ::-webkit-scrollbar {{ width: 6px; }}
      ::-webkit-scrollbar-thumb {{ background: rgba(0,0,0,0.2); border-radius: 3px; }}
    `;
}})();
";
    }

    private static string EscapeJsString(string s)
        => s.Replace("\\", "\\\\").Replace("'", "\\'").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r");

    private (string bg, string color, string headingColor, string accent) GetThemeColors()
    {
        return SelectedTheme switch
        {
            ThemeType.Day => ("#F5F0E8", "#333333", "#2C2C3A", "#4A90D9"),
            ThemeType.Night => ("#1A1A2E", "#C8C8D4", "#E0E0EC", "#6BB3F0"),
            ThemeType.EyeCare => ("#C7EDCC", "#3A4A3A", "#2C3A2C", "#4CAF50"),
            _ => ("#F5F0E8", "#333333", "#2C2C3A", "#4A90D9")
        };
    }

    private string GetFontFamilyCss()
    {
        return SelectedFontFamily switch
        {
            FontFamilyOption.Serif => "\"Noto Serif CJK SC\", \"SimSun\", Georgia, serif",
            FontFamilyOption.SansSerif => "\"Microsoft YaHei\", \"PingFang SC\", Arial, sans-serif",
            FontFamilyOption.Monospace => "\"JetBrains Mono\", \"Courier New\", monospace",
            _ => "\"Noto Serif CJK SC\", \"SimSun\", Georgia, serif"
        };
    }

    private string GetLineHeightCss()
        => SelectedLineHeight switch
        {
            LineHeightOption.Compact => "1.4",
            LineHeightOption.Normal => "1.85",
            LineHeightOption.Loose => "2.2",
            _ => "1.85"
        };

    private string GetPageWidthCss()
        => SelectedPageWidth switch
        {
            PageWidthOption.Narrow => "680px",
            PageWidthOption.Medium => "820px",
            PageWidthOption.Wide => "960px",
            _ => "820px"
        };

    // ─── 私有辅助 ───

    /// <summary>从 XHTML 内容中提取 &lt;body&gt; 内部内容</summary>
    private static string ExtractBodyContent(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return "<p style='color:#888;text-align:center;margin-top:60px;'>（空章节）</p>";

        // 尝试匹配 <body ...>...</body>
        var bodyMatch = System.Text.RegularExpressions.Regex.Match(
            html, @"<body[^>]*>(.*)</body>",
            System.Text.RegularExpressions.RegexOptions.Singleline
                | System.Text.RegularExpressions.RegexOptions.IgnoreCase
                | System.Text.RegularExpressions.RegexOptions.ExplicitCapture);

        if (bodyMatch.Success && bodyMatch.Groups.Count > 1)
        {
            var extracted = bodyMatch.Groups[1].Value.Trim();
            if (!string.IsNullOrWhiteSpace(extracted))
                return extracted;
        }

        // 如果内容本身没有 <body> 标签（如纯片段），
        // 检查是否已经是完整的 HTML 文档结构
        if (html.Contains("<html", StringComparison.OrdinalIgnoreCase) ||
            html.Contains("<!DOCTYPE", StringComparison.OrdinalIgnoreCase))
        {
            // 已经是完整 HTML 文档，尝试提取 <body> 中的内容
            // 再次尝试更宽松的匹配
            var relaxedMatch = System.Text.RegularExpressions.Regex.Match(
                html, @"<body\b[^>]*>([\s\S]*)</body\s*>",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (relaxedMatch.Success && relaxedMatch.Groups.Count > 1)
            {
                var extracted = relaxedMatch.Groups[1].Value.Trim();
                if (!string.IsNullOrWhiteSpace(extracted))
                    return extracted;
            }
        }

        // fallback：直接返回原内容（但移除 XML 声明避免混乱）
        var cleaned = System.Text.RegularExpressions.Regex.Replace(
            html, @"<\?xml[^>]*\?>", "", 
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return cleaned.Trim();
    }

}
