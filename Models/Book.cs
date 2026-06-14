namespace EpubRead.Models;

/// <summary>
/// 书架上的书籍条目，对应 SQLite Books 表中的一行
/// </summary>
public class Book
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = "未知书名";
    public string Author { get; set; } = "未知作者";
    public string FilePath { get; set; } = string.Empty;
    public string? CoverPath { get; set; }
    public DateTime ImportDate { get; set; } = DateTime.Now;
    public int LastReadChapterIndex { get; set; } = -1;
    public int TotalChapters { get; set; }
    public double ReadProgressPercent =>
        TotalChapters > 0 && LastReadChapterIndex >= 0
            ? Math.Round((double)(LastReadChapterIndex + 1) / TotalChapters * 100, 1)
            : 0;
}
