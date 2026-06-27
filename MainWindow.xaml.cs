using System.IO;
using System.Windows;
using EpubRead.Models;
using EpubRead.Services;
using EpubRead.ViewModels;
using EpubRead.Views;

namespace EpubRead;

public partial class MainWindow : Window
{
    private readonly BookshelfService _bookshelfService;
    private readonly EpubParser _epubParser;
    private readonly ReadingSettingsService _settingsService;
    private readonly NoteService _noteService;
    private readonly string _appDataDir;
    private BookshelfViewModel? _bookshelfViewModel;

    public MainWindow(BookshelfService bookshelfService, EpubParser epubParser, ReadingSettingsService settingsService, NoteService noteService, string appDataDir)
    {
        InitializeComponent();
        _bookshelfService = bookshelfService;
        _epubParser = epubParser;
        _settingsService = settingsService;
        _noteService = noteService;
        _appDataDir = appDataDir;

        // 加载书架页面
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        NavigateToBookshelf();
    }

    public void NavigateToBookshelf()
    {
        _bookshelfViewModel = new BookshelfViewModel(_bookshelfService, _epubParser, _appDataDir);
        _bookshelfViewModel.OpenBookRequested += OnOpenBookRequested;

        var page = new BookshelfPage { DataContext = _bookshelfViewModel };
        MainFrame.Navigate(page);
    }

    private void OnOpenBookRequested(object? sender, Book book)
    {
        // 检查文件是否存在，避免崩溃
        if (!File.Exists(book.FilePath))
        {
            MessageBox.Show(
                $"书籍文件不存在：\n\n{book.FilePath}\n\n文件可能已被移动、重命名或删除。\n\n您可以从书架中移除该书籍。",
                "无法打开书籍",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        var readerViewModel = new ReaderViewModel(_epubParser, _settingsService, _noteService, book.Id);
        var readerPage = new ReaderPage(readerViewModel);

        // 订阅返回事件
        readerViewModel.GoBackRequested += (s, e) =>
        {
            if (MainFrame.CanGoBack)
                MainFrame.GoBack();
        };

        // 订阅进度变更，持久化保存
        readerViewModel.ProgressChanged += (s, args) =>
        {
            _bookshelfService.UpdateBookProgress(book.Id, args.chapterIndex, args.totalChapters);
            // 同步更新内存中 Book 对象的进度，以便书架页面刷新时显示
            book.LastReadChapterIndex = args.chapterIndex;
            book.TotalChapters = args.totalChapters;
        };

        MainFrame.Navigate(readerPage);
        readerPage.OpenBook(book);
    }
}
