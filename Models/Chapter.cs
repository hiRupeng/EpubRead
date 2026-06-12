namespace EpubRead.Models;

/// <summary>
/// EPUB 章节结构，包含标题、相对路径、HTML 内容和阅读顺序
/// </summary>
public class Chapter
{
    public string Title { get; set; } = string.Empty;
    /// <summary>
    /// 相对于 OEBPS 目录的路径
    /// </summary>
    public string Href { get; set; } = string.Empty;
    /// <summary>
    /// 章节 HTML 内容（按需加载）
    /// </summary>
    public string? Content { get; set; }
    /// <summary>
    /// 在 spine 中的阅读顺序
    /// </summary>
    public int Order { get; set; }
}
