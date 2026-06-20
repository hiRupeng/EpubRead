using System.Windows;
using System.Windows.Input;
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
    private readonly string _appDataDir;
    private BookshelfViewModel? _bookshelfViewModel;

    public MainWindow(BookshelfService bookshelfService, EpubParser epubParser, ReadingSettingsService settingsService, string appDataDir)
    {
        InitializeComponent();
        _bookshelfService = bookshelfService;
        _epubParser = epubParser;
        _settingsService = settingsService;
        _appDataDir = appDataDir;

        // 自定义标题栏拖拽
        MouseDown += OnTitleBarMouseDown;

        // 加载书架页面
        Loaded += OnLoaded;
    }

    private void OnTitleBarMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;

        if (e.ClickCount >= 2)
        {
            // 双击标题栏：最大化 / 还原
            WindowState = WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
        }
        else
        {
            DragMove();
        }
    }

    private void OnMinimizeClick(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void OnMaximizeClick(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
    }

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        Close();
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
        var readerViewModel = new ReaderViewModel(_epubParser, _settingsService);
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
