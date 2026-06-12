using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Input;
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

    public ObservableCollection<Book> Books { get; } = [];

    public BookshelfViewModel(BookshelfService bookshelfService, EpubParser epubParser, string appDataDir)
    {
        _bookshelfService = bookshelfService;
        _epubParser = epubParser;
        _coversDir = Path.Combine(appDataDir, "covers");
        Directory.CreateDirectory(_coversDir);

        // 加载已有书籍
        LoadBooks();
    }

    private void LoadBooks()
    {
        Books.Clear();
        var books = _bookshelfService.GetAllBooks();
        foreach (var book in books)
            Books.Add(book);
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

        foreach (var filePath in dialog.FileNames)
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
                Books.Insert(0, book);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"导入失败：{Path.GetFileName(filePath)}\n{ex.Message}", "导入错误",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
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

    public event EventHandler<Book>? OpenBookRequested;

    [RelayCommand]
    private void OpenBook(Book? book)
    {
        if (book != null)
            OpenBookRequested?.Invoke(this, book);
    }
}
