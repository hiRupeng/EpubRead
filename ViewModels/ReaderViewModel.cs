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
    private EpubBook? _epubBook;
    private string _filePath = string.Empty;

    public ObservableCollection<Chapter> Chapters { get; } = [];

    [ObservableProperty]
    private Chapter? _currentChapter;

    [ObservableProperty]
    private string _bookTitle = string.Empty;

    [ObservableProperty]
    private string _progressText = string.Empty;

    [ObservableProperty]
    private bool _isTocOpen;

    [ObservableProperty]
    private bool _hasPrev;

    [ObservableProperty]
    private bool _hasNext;

    public event EventHandler<string>? ContentRequested;
    public event EventHandler<string?>? ScrollToAnchorRequested;
    public event EventHandler? GoBackRequested;
    public event EventHandler<(int chapterIndex, int totalChapters)>? ProgressChanged;

    public ReaderViewModel(EpubParser epubParser)
    {
        _epubParser = epubParser;
    }

    /// <summary>
    /// 加载 EPUB 书籍并导航到上次阅读位置或第一章
    /// </summary>
    public void LoadBook(Book book)
    {
        _filePath = book.FilePath;
        BookTitle = book.Title;

        _epubBook = _epubParser.Parse(_filePath);

        Chapters.Clear();
        foreach (var ch in _epubBook.Chapters)
            Chapters.Add(ch);

        if (Chapters.Count == 0) return;

        // 恢复上次阅读位置
        int startIndex = book.LastReadChapterIndex;
        if (startIndex >= 0 && startIndex < Chapters.Count)
            NavigateToChapter(Chapters[startIndex]);
        else
            NavigateToChapter(Chapters[0]);
    }

    [RelayCommand]
    private void ToggleToc()
    {
        IsTocOpen = !IsTocOpen;
    }

    [RelayCommand]
    private void NavigateToChapter(Chapter? chapter)
    {
        if (chapter == null || _epubBook == null) return;

        // 强制重新加载内容
        chapter.Content = _epubParser.LoadChapterContent(
            _filePath, _epubBook.BasePath, chapter.Href);

        CurrentChapter = chapter;

        // 注入阅读样式后用 WebView2 渲染
        var styledContent = WrapWithReadingStyle(chapter.Content ?? "<p>内容为空</p>");
        ContentRequested?.Invoke(this, styledContent);

        // 如果有锚点，内容渲染后滚动到目标位置
        if (!string.IsNullOrEmpty(chapter.Anchor))
        {
            ScrollToAnchorRequested?.Invoke(this, chapter.Anchor);
        }

        UpdateNavigation();

        // 通知外部保存进度
        var idx = Chapters.IndexOf(chapter);
        if (idx >= 0)
            ProgressChanged?.Invoke(this, (idx, Chapters.Count));
    }

    [RelayCommand]
    private void NavigatePrev()
    {
        if (CurrentChapter == null) return;
        var idx = Chapters.IndexOf(CurrentChapter);
        if (idx > 0)
            NavigateToChapter(Chapters[idx - 1]);
    }

    [RelayCommand]
    private void NavigateNext()
    {
        if (CurrentChapter == null) return;
        var idx = Chapters.IndexOf(CurrentChapter);
        if (idx < Chapters.Count - 1)
            NavigateToChapter(Chapters[idx + 1]);
    }

    [RelayCommand]
    private void GoBack()
    {
        GoBackRequested?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateNavigation()
    {
        if (CurrentChapter == null) return;
        var idx = Chapters.IndexOf(CurrentChapter);

        HasPrev = idx > 0;
        HasNext = idx < Chapters.Count - 1;
        ProgressText = $"第 {idx + 1} / {Chapters.Count} 章";
    }

    private static string WrapWithReadingStyle(string content)
    {
        // 如果原始内容包含完整 HTML 文档，提取 <body> 内部内容
        // 避免双重 <html> 嵌套导致 WebView2 渲染空白
        var bodyContent = ExtractBodyContent(content);

        return $$"""
            <!DOCTYPE html>
            <html>
            <head>
            <meta charset="utf-8">
            <style>
              * { margin: 0; padding: 0; box-sizing: border-box; }
              body {
                font-family: "Microsoft YaHei", "SimSun", "Noto Serif CJK SC", Georgia, serif;
                font-size: 17px;
                line-height: 1.85;
                color: #333;
                background: #F5F0E8;
                padding: 48px 32px;
                max-width: 820px;
                margin: 0 auto;
              }
              h1, h2, h3 { color: #2C2C3A; margin: 1.2em 0 0.6em; }
              h1 { font-size: 1.6em; }
              h2 { font-size: 1.3em; }
              p { margin: 0.8em 0; text-indent: 2em; }
              img { max-width: 100%; height: auto; display: block; margin: 12px auto; border-radius: 6px; }
              blockquote { border-left: 4px solid #4A90D9; padding-left: 16px; margin: 1em 0; color: #555; }
              a { color: #4A90D9; }
              ::selection { background: #4A90D9; color: #fff; }
              ::-webkit-scrollbar { width: 6px; }
              ::-webkit-scrollbar-thumb { background: #ccc; border-radius: 3px; }
            </style>
            </head>
            <body>
            {{bodyContent}}
            </body>
            </html>
            """;
    }

    /// <summary>
    /// 从 XHTML 内容中提取 &lt;body&gt; 内部内容
    /// 如果内容不是完整文档，则原样返回
    /// </summary>
    private static string ExtractBodyContent(string html)
    {
        // 尝试使用正则提取 body 内容
        var bodyMatch = System.Text.RegularExpressions.Regex.Match(
            html, @"<body[^>]*>(.*?)</body>",
            System.Text.RegularExpressions.RegexOptions.Singleline | System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        if (bodyMatch.Success && bodyMatch.Groups.Count > 1)
        {
            var extracted = bodyMatch.Groups[1].Value.Trim();
            if (!string.IsNullOrEmpty(extracted))
                return extracted;
        }

        // 不是完整文档，原样返回
        return html;
    }
}
