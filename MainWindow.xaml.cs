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
    private readonly string _appDataDir;
    private BookshelfViewModel? _bookshelfViewModel;

    public MainWindow(BookshelfService bookshelfService, EpubParser epubParser, string appDataDir)
    {
        InitializeComponent();
        _bookshelfService = bookshelfService;
        _epubParser = epubParser;
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
        var readerViewModel = new ReaderViewModel(_epubParser);
        var readerPage = new ReaderPage(readerViewModel);

        // 订阅返回事件
        readerViewModel.GoBackRequested += (s, e) =>
        {
            if (MainFrame.CanGoBack)
                MainFrame.GoBack();
        };

        MainFrame.Navigate(readerPage);
        readerPage.OpenBook(book);
    }
}