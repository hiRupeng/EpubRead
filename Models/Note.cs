namespace EpubRead.Models;

/// <summary>
/// 高亮笔记，关联到具体书籍与章节，用于在阅读区还原选区高亮。
/// 使用全局字符偏移定位（遍历 .reading-card 内所有文本节点累加），不依赖 DOM 路径。
/// </summary>
public class Note
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>关联书籍 Id（Book.Id）</summary>
    public string BookId { get; set; } = string.Empty;

    /// <summary>章节文件路径（Chapter.Href），用于按章恢复高亮</summary>
    public string ChapterHref { get; set; } = string.Empty;

    /// <summary>起点在 .reading-card 内的全局字符偏移</summary>
    public int StartOffset { get; set; }

    /// <summary>终点在 .reading-card 内的全局字符偏移</summary>
    public int EndOffset { get; set; }

    /// <summary>选中的原文（用于恢复时校验是否仍匹配）</summary>
    public string SelectedText { get; set; } = string.Empty;

    /// <summary>高亮颜色（CSS 颜色值）</summary>
    public string Color { get; set; } = "#FFE082";

    public DateTime CreatedAt { get; set; } = DateTime.Now;
}
