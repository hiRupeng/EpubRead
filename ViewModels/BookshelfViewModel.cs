using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EpubRead.Models;
using EpubRead.Services;
using Microsoft.Win32;

namespace EpubRead.ViewModels;

public partial class BookshelfViewModel : ObservableObject
{
    private readonly BookshelfService _bookshelfService;
    private readonly EpubParser _epubParser;
    private readonly string _coversDir;
    private ICollectionView? _booksView;

    public ObservableCollection<Book> Books { get; } = [];

    /// <summary>
    /// 搜索文本，实时过滤书架
    /// </summary>
    [ObservableProperty]
    private string _searchText = string.Empty;

    /// <summary>
    /// 过滤后的书架视图，供 UI 绑定
    /// </summary>
    public ICollectionView? BooksView
    {
        get
        {
            if (_booksView == null)
            {
                _booksView = CollectionViewSource.GetDefaultView(Books);
                _booksView.Filter = FilterBook;
            }
            return _booksView;
        }
    }

    public BookshelfViewModel(BookshelfService bookshelfService, EpubParser epubParser, string appDataDir)
    {
        _bookshelfService = bookshelfService;
        _epubParser = epubParser;
        _coversDir = Path.Combine(appDataDir, "covers");
        Directory.CreateDirectory(_coversDir);

        // 加载已有书籍
        LoadBooks();
    }

    /// <summary>
    /// 搜索文本变化时刷新过滤
    /// </summary>
    partial void OnSearchTextChanged(string value)
    {
        BooksView?.Refresh();
    }

    private bool FilterBook(object obj)
    {
        if (string.IsNullOrWhiteSpace(SearchText))
            return true;

        if (obj is not Book book)
            return false;

        var keyword = SearchText.Trim();
        return book.Title.Contains(keyword, StringComparison.OrdinalIgnoreCase)
            || book.Author.Contains(keyword, StringComparison.OrdinalIgnoreCase);
    }

    private void LoadBooks()
    {
        Books.Clear();
        var books = _bookshelfService.GetAllBooks();
        foreach (var book in books)
            Books.Add(book);
        BooksView?.Refresh();
    }

    [RelayCommand]
    private void ImportBook()
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择 EPUB 文件",
            Filter = "EPUB 文件 (*.epub)|*.epub|所有文件 (*.*)|*.*",
            Multiselect = true
        };

        if (dialog.ShowDialog() != true) return;

        ImportFiles(dialog.FileNames);
    }

    /// <summary>
    /// 批量导入 EPUB 文件（供拖拽/文件夹导入/文件对话框共用）
    /// </summary>
    public void ImportFiles(IEnumerable<string> filePaths)
    {
        foreach (var filePath in filePaths)
        {
            try
            {
                // 检查是否已导入
                if (Books.Any(b => b.FilePath.Equals(filePath, StringComparison.OrdinalIgnoreCase)))
                    continue;

                // 生成封面缓存路径
                var coverFileName = $"{Path.GetFileNameWithoutExtension(filePath)}_{Guid.NewGuid():N}".GetHashCode().ToString("X8") + ".png";
                var coverPath = Path.Combine(_coversDir, coverFileName);

                // 解析 EPUB 并缓存封面
                EpubBook epubBook;
                using (var fs = new FileStream(coverPath, FileMode.Create))
                {
                    epubBook = _epubParser.Parse(filePath, fs);
                }

                // 如果没有封面图片，删除空文件
                if (epubBook.CoverImage == null && File.Exists(coverPath))
                {
                    File.Delete(coverPath);
                    coverPath = null;
                }

                var book = new Book
                {
                    Title = epubBook.Title,
                    Author = epubBook.Author,
                    FilePath = filePath,
                    CoverPath = coverPath,
                    ImportDate = DateTime.Now
                };

                _bookshelfService.AddBook(book);

                // Dispatch to UI thread for collection update
                Application.Current.Dispatcher.Invoke(() => Books.Insert(0, book));
            }
            catch (Exception ex)
            {
                Application.Current.Dispatcher.Invoke(() =>
                    MessageBox.Show($"导入失败：{Path.GetFileName(filePath)}\n{ex.Message}", "导入错误",
                        MessageBoxButton.OK, MessageBoxImage.Warning));
            }
        }
    }

    [RelayCommand]
    private void DeleteBook(Book? book)
    {
        if (book == null) return;

        var result = MessageBox.Show(
            $"确定要从书架移除「{book.Title}」吗？（原始文件不会被删除）",
            "移除书籍", MessageBoxButton.YesNo, MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes) return;

        _bookshelfService.RemoveBook(book.Id, _coversDir);
        Books.Remove(book);
    }

    [RelayCommand]
    private void ImportFolder()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "选择包含 EPUB 文件的文件夹"
        };

        if (dialog.ShowDialog() != true) return;

        var folderPath = dialog.FolderName;
        if (string.IsNullOrEmpty(folderPath) || !Directory.Exists(folderPath)) return;

        // 递归搜索所有 .epub 文件
        var epubFiles = Directory.EnumerateFiles(
            folderPath, "*.epub", SearchOption.AllDirectories).ToList();

        if (epubFiles.Count == 0)
        {
            MessageBox.Show("所选文件夹中未发现 EPUB 文件。", "导入文件夹",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        ImportFiles(epubFiles);
    }

    public event EventHandler<Book>? OpenBookRequested;

    [RelayCommand]
    private void OpenBook(Book? book)
    {
        if (book != null)
            OpenBookRequested?.Invoke(this, book);
    }
}
