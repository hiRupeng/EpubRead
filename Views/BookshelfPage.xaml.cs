using System.Windows;
using System.Windows.Controls;
using EpubRead.ViewModels;

namespace EpubRead.Views;

public partial class BookshelfPage : Page
{
    private bool _isValidDrag;

    public BookshelfPage()
    {
        InitializeComponent();
    }

    private void OnDragEnter(object sender, DragEventArgs e)
    {
        _isValidDrag = HasEpubFiles(e);
        if (_isValidDrag)
        {
            e.Effects = DragDropEffects.Copy;
            DragOverlay.Visibility = Visibility.Visible;
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
        e.Handled = true;
    }

    private void OnDragLeave(object sender, DragEventArgs e)
    {
        DragOverlay.Visibility = Visibility.Collapsed;
        _isValidDrag = false;
        e.Handled = true;
    }

    private async void OnDrop(object sender, DragEventArgs e)
    {
        DragOverlay.Visibility = Visibility.Collapsed;
        _isValidDrag = false;

        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;

        var files = (string[]?)e.Data.GetData(DataFormats.FileDrop);
        if (files == null || files.Length == 0) return;

        var epubFiles = files.Where(f =>
            f.EndsWith(".epub", StringComparison.OrdinalIgnoreCase)).ToList();

        if (epubFiles.Count == 0) return;

        if (DataContext is BookshelfViewModel vm)
        {
            await Task.Run(() => vm.ImportFiles(epubFiles));
        }

        e.Handled = true;
    }

    private static bool HasEpubFiles(DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop))
            return false;

        var files = (string[]?)e.Data.GetData(DataFormats.FileDrop);
        return files != null && files.Any(f =>
            f.EndsWith(".epub", StringComparison.OrdinalIgnoreCase));
    }
}
