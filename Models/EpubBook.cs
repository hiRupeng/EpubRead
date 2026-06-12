namespace EpubRead.Models;

/// <summary>
/// EPUB 解析结果，包含元数据、封面数据、章节列表及资源基础路径
/// </summary>
public class EpubBook
{
    public string Title { get; set; } = "未知书名";
    public string Author { get; set; } = "未知作者";
    public byte[]? CoverImage { get; set; }
    public List<Chapter> Chapters { get; set; } = [];
    /// <summary>
    /// EPUB 内 OPF 所在目录的基础路径（如 OEBPS/），用于拼接资源路径
    /// </summary>
    public string BasePath { get; set; } = string.Empty;
}
