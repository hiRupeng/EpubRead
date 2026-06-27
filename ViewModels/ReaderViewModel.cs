using System.Collections.ObjectModel;
using System.IO;
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

    // ─── 阅读设置 ───

    [ObservableProperty]
    private ThemeType _selectedTheme = ThemeType.Day;

    /// <summary>当前界面主题配色（随 SelectedTheme 自动更新）</summary>
    [ObservableProperty]
    private ReaderUITheme _uiTheme = ReaderUITheme.Build(ThemeType.Day);

    /// <summary>主题变化时同步更新界面配色</summary>
    partial void OnSelectedThemeChanged(ThemeType value)
        => UiTheme = ReaderUITheme.Build(value);

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
    /// <summary>章节内容加载完成，触发高亮恢复（传递 JSON 笔记数组）</summary>
    public event EventHandler<string>? HighlightsRestoreRequested;
    /// <summary>创建高亮（传递 JSON 选区信息），由 Page 上报选区后触发</summary>
    public event EventHandler<string>? HighlightCreated;
    /// <summary>删除高亮（传递笔记 Id）</summary>
    public event EventHandler<string>? HighlightDeleted;

    private readonly NoteService _noteService;
    private readonly string _bookId;

    public ReaderViewModel(EpubParser epubParser, ReadingSettingsService settingsService, NoteService noteService, string bookId)
    {
        _epubParser = epubParser;
        _settingsService = settingsService;
        _noteService = noteService;
        _bookId = bookId;
    }

    // ─── 书籍加载 ───

    /// <summary>
    /// 加载 EPUB 书籍并导航到上次阅读位置或第一章
    /// </summary>
    public bool LoadBook(Book book)
    {
        _filePath = book.FilePath;
        BookTitle = book.Title;

        // 检查文件是否存在
        if (!File.Exists(_filePath))
        {
            BookTitle = "文件不存在";
            ContentRequested?.Invoke(this, GenerateErrorHtml(
                $"<b>文件不存在</b><br/><br/>路径：{System.Net.WebUtility.HtmlEncode(_filePath)}<br/><br/>文件可能已被移动、重命名或删除。"));
            return false;
        }

        try
        {
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

            if (FlatChapters.Count == 0)
            {
                ContentRequested?.Invoke(this, GenerateErrorHtml("该 EPUB 文件中没有可读取的章节内容。"));
                return false;
            }

            // 恢复上次阅读位置
            int startIndex = book.LastReadChapterIndex;
            if (startIndex >= 0 && startIndex < FlatChapters.Count)
                NavigateToChapter(FlatChapters[startIndex]);
            else
                NavigateToChapter(FlatChapters[0]);

            return true;
        }
        catch (Exception ex)
        {
            BookTitle = "加载失败";
            ContentRequested?.Invoke(this, GenerateErrorHtml(
                $"<b>加载 EPUB 失败</b><br/><br/>{System.Net.WebUtility.HtmlEncode(ex.Message)}"));
            return false;
        }
    }

    /// <summary>
    /// 生成错误提示页面 HTML
    /// </summary>
    private string GenerateErrorHtml(string message)
    {
        var (desk, _, color, _, _) = GetThemeColors();
        return $$"""
            <!DOCTYPE html>
            <html>
            <head><meta charset="utf-8"></head>
            <body style="background:{{desk}};display:flex;align-items:center;justify-content:center;min-height:100vh;margin:0;">
            <div style="text-align:center;color:{{color}};font-family:'Microsoft YaHei',sans-serif;
                        padding:40px;max-width:600px;line-height:1.8;">
                <div style="font-size:48px;margin-bottom:16px;opacity:0.5;">⚠️</div>
                <div style="font-size:16px;opacity:0.8;">{{message}}</div>
            </div>
            </body>
            </html>
            """;
    }

    private void ApplySettings(ReadingSettings s)
    {
        SelectedTheme = s.Theme;
        FontSize = s.FontSize;
        SelectedFontFamily = s.FontFamily;
        SelectedLineHeight = s.LineHeight;
        SelectedPageWidth = s.PageWidth;
    }

    private ReadingSettings CollectSettings() => new()
    {
        Theme = SelectedTheme,
        FontSize = FontSize,
        FontFamily = SelectedFontFamily,
        LineHeight = SelectedLineHeight,
        PageWidth = SelectedPageWidth
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

        // 加载完整章节内容：一个逻辑章节可能跨多个 spine 文件，
        // 需要从当前文件加载到下一个目录条目对应的文件为止
        var fileHrefs = ComputeFullContentFiles(chapter);
        string bodyContent;

        if (fileHrefs.Count <= 1)
        {
            // 单文件：走原有路径
            var raw = _epubParser.LoadChapterContent(_filePath, _epubBook.BasePath, chapter.Href);
            bodyContent = ExtractBodyContent(raw);
        }
        else
        {
            // 多文件：分别提取 body 后拼接，避免多个 <body> 标签干扰正则
            var rawParts = _epubParser.LoadMultiFileRawContent(
                _filePath, _epubBook.BasePath, fileHrefs);
            bodyContent = string.Join("\n", rawParts.Select(ExtractBodyContent));
        }

        chapter.Content = bodyContent;
        CurrentChapter = chapter;

        var styledContent = GenerateReadingStyleFromBody(bodyContent, chapter.Href);
        ContentRequested?.Invoke(this, styledContent);

        if (!string.IsNullOrEmpty(chapter.Anchor))
        {
            ScrollToAnchorRequested?.Invoke(this, chapter.Anchor);
        }

        UpdateNavigation();

        var idx = FlatChapters.IndexOf(chapter);
        if (idx >= 0)
            ProgressChanged?.Invoke(this, (idx, FlatChapters.Count));

        // 触发高亮恢复：查询本章笔记并通过 Page 注入渲染
        RestoreHighlights(chapter.Href);
    }

    // ─── 高亮笔记 ───

    /// <summary>
    /// 恢复指定章节的高亮：查询数据库并通过事件通知 Page 注入渲染脚本
    /// </summary>
    private void RestoreHighlights(string chapterHref)
    {
        var notes = _noteService.GetNotes(_bookId, chapterHref);
        if (notes.Count == 0) return;

        var json = System.Text.Json.JsonSerializer.Serialize(notes.Select(n => new
        {
            id = n.Id,
            startOffset = n.StartOffset,
            endOffset = n.EndOffset,
            text = n.SelectedText,
            color = n.Color
        }));
        HighlightsRestoreRequested?.Invoke(this, json);
    }

    /// <summary>
    /// 处理来自 HTML 的创建高亮请求（选区信息 JSON）
    /// </summary>
    public void CreateHighlight(string selectionJson)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(selectionJson);
            var root = doc.RootElement;
            var note = new Note
            {
                BookId = _bookId,
                ChapterHref = CurrentChapter?.Href ?? "",
                StartOffset = root.GetProperty("startOffset").GetInt32(),
                EndOffset = root.GetProperty("endOffset").GetInt32(),
                SelectedText = root.GetProperty("text").GetString() ?? "",
                Color = root.TryGetProperty("color", out var c) && c.ValueKind == System.Text.Json.JsonValueKind.String
                    ? c.GetString() ?? "#FFE082"
                    : "#FFE082",
                CreatedAt = DateTime.Now
            };
            _noteService.SaveNote(note);

            // 通知 Page 渲染该高亮
            var renderJson = System.Text.Json.JsonSerializer.Serialize(new
            {
                id = note.Id,
                startOffset = note.StartOffset,
                endOffset = note.EndOffset,
                text = note.SelectedText,
                color = note.Color
            });
            HighlightCreated?.Invoke(this, renderJson);
        }
        catch
        {
            // 选区数据异常，忽略
        }
    }

    /// <summary>
    /// 删除指定高亮笔记
    /// </summary>
    public void DeleteHighlight(string noteId)
    {
        _noteService.DeleteNote(noteId);
        HighlightDeleted?.Invoke(this, noteId);
    }

    /// <summary>
    /// 计算某章节应加载的所有 spine 文件列表。
    /// 从该章节对应的 spine 文件开始，加载后续文件直到遇到下一个目录条目对应的文件为止。
    /// 这样可以正确加载被拆分到多个文件中的完整章节内容。
    /// </summary>
    private List<string> ComputeFullContentFiles(Chapter chapter)
    {
        if (_epubBook == null || _epubBook.SpineFiles.Count == 0)
            return [chapter.Href];

        return ComputeFullContentFilesByHref(chapter.Href);
    }

    /// <summary>
    /// 根据 href 计算应加载的所有 spine 文件列表（兜底场景使用）。
    /// </summary>
    private List<string> ComputeFullContentFilesByHref(string href)
    {
        if (_epubBook == null || _epubBook.SpineFiles.Count == 0)
            return [href];

        var spineFiles = _epubBook.SpineFiles;

        // 找到当前文件在 spine 中的位置
        var startIndex = spineFiles.FindIndex(f =>
            string.Equals(f, href, StringComparison.OrdinalIgnoreCase));
        if (startIndex < 0)
            return [href];

        // 收集所有目录条目对应的文件 href（用于确定章节边界）
        var tocStartFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var ch in _epubBook.FlatChapters)
        {
            if (!string.IsNullOrEmpty(ch.Href))
                tocStartFiles.Add(ch.Href);
        }

        // 从 startIndex 开始，加载后续文件直到遇到另一个目录条目的起始文件
        var result = new List<string> { spineFiles[startIndex] };
        for (int i = startIndex + 1; i < spineFiles.Count; i++)
        {
            if (tocStartFiles.Contains(spineFiles[i]))
                break;
            result.Add(spineFiles[i]);
        }

        return result;
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

        // 2) 兜底：直接按路径加载章节内容（含跨文件拼接）
        foreach (var candidate in candidates)
        {
            var simpleHref = candidate;
            if (simpleHref.StartsWith(_epubBook.BasePath, StringComparison.OrdinalIgnoreCase))
                simpleHref = simpleHref[_epubBook.BasePath.Length..];

            // 规范化路径用于在 spine 中查找
            var normalizedHref = EpubParser.NormalizePath(simpleHref);

            // 计算该 href 在 spine 中的完整文件范围
            var fileHrefs = ComputeFullContentFilesByHref(normalizedHref);
            string bodyContent;

            if (fileHrefs.Count <= 1)
            {
                var raw = _epubParser.LoadChapterContent(_filePath, _epubBook.BasePath, simpleHref);
                if (string.IsNullOrEmpty(raw) ||
                    raw.Equals("<p>内容加载失败</p>", StringComparison.Ordinal))
                    continue;
                bodyContent = ExtractBodyContent(raw);
            }
            else
            {
                var rawParts = _epubParser.LoadMultiFileRawContent(
                    _filePath, _epubBook.BasePath, fileHrefs);
                if (rawParts.Count == 0) continue;
                bodyContent = string.Join("\n", rawParts.Select(ExtractBodyContent));
            }

            if (!string.IsNullOrWhiteSpace(bodyContent))
            {
                var styled = GenerateReadingStyleFromBody(bodyContent, simpleHref);
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
        NavigateToPrevSpineRoot();
    }

    [RelayCommand]
    private void NavigateNext()
    {
        if (CurrentChapter == null) return;
        NavigateToNextSpineRoot();
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

        var group = GetCurrentSpineGroup();
        var firstIdx = group.Count > 0 ? FlatChapters.IndexOf(group[0]) : -1;
        var totalFiles = FlatChapters.Count(c => c.IsSpineRoot);
        var currentFileIndex = group.Count > 0
            ? FlatChapters.TakeWhile(c => c != group[0]).Count(c => c.IsSpineRoot)
            : 0;

        HasPrev = firstIdx > 0;
        HasNext = firstIdx < FlatChapters.Count - 1;
        ProgressText = $"{currentFileIndex + 1} / {totalFiles}";
        TopProgress = totalFiles > 0 ? (double)(currentFileIndex + 1) / totalFiles : 0;
        ProgressPercentText = $"{(int)(TopProgress * 100)}%";
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
        return GenerateReadingStyleFromBody(bodyContent, fileHref);
    }

    /// <summary>
    /// 从已提取的 body 内容生成完整的阅读页面 HTML。
    /// 用于多文件拼接场景，避免对拼接后的内容重复提取 body。
    /// </summary>
    private string GenerateReadingStyleFromBody(string bodyContent, string? fileHref = null)
    {
        var (desk, bg, color, hColor, blockquoteColor) = GetThemeColors();
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
              html { background: {{desk}}; }
              body {
                background: {{desk}};
                margin: 0;
                min-height: 100vh;
              }
              .reading-card {
                font-family: {{fontFamily}};
                font-size: {{FontSize}}px;
                line-height: {{lineHeight}};
                color: {{color}};
                background: {{bg}};
                max-width: {{pageWidth}};
                margin: 0 auto;
                padding: 48px 32px;
                min-height: 100vh;
                border-radius: 10px;
                box-shadow: 0 8px 28px rgba(0,0,0,0.22);
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
            <div class="reading-card">
            {{bodyContent}}
            </div>
            </body>
            </html>
            """;
    }

    /// <summary>
    /// 生成用于注入当前页面的 JavaScript 代码（设置变化时使用，不重载页面）
    /// </summary>
    private string GenerateStyleScript()
    {
        var (desk, bg, color, hColor, blockquoteColor) = GetThemeColors();
        var fontFamily = GetFontFamilyCss();
        var lineHeight = GetLineHeightCss();
        var pageWidth = GetPageWidthCss();

        // 转义引号，防止 JavaScript 字符串问题
        var safeDesk = EscapeJsString(desk);
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
      html {{ background: {safeDesk}; }}
      body {{
        background: {safeDesk};
        margin: 0;
        min-height: 100vh;
      }}
      .reading-card {{
        font-family: {safeFont};
        font-size: {FontSize}px;
        line-height: {lineHeight};
        color: {safeColor};
        background: {safeBg};
        max-width: {safeWidth};
        margin: 0 auto;
        padding: 48px 32px;
        min-height: 100vh;
        border-radius: 10px;
        box-shadow: 0 8px 28px rgba(0,0,0,0.22);
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

    private (string desk, string bg, string color, string headingColor, string accent) GetThemeColors()
    {
        return SelectedTheme switch
        {
            ThemeType.Day => ("#E0E0E0", "#FAFAFA", "#1F1F1F", "#1F1F1F", "#0078D4"),
            ThemeType.Night => ("#161616", "#202020", "#D4D4D4", "#FFFFFF", "#60CDFF"),
            ThemeType.EyeCare => ("#C9BB8E", "#F5E6C8", "#3D3522", "#2D2519", "#8B6914"),
            _ => ("#E0E0E0", "#FAFAFA", "#1F1F1F", "#1F1F1F", "#0078D4")
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
