namespace EpubRead.Models;

/// <summary>
/// EPUB 解析结果，包含元数据、封面数据、章节列表及资源基础路径
/// </summary>
public class EpubBook
{
    public string Title { get; set; } = "未知书名";
    public string Author { get; set; } = "未知作者";
    public byte[]? CoverImage { get; set; }

    /// <summary>
    /// 层级章节树（用于目录面板展示）
    /// </summary>
    public List<Chapter> Chapters { get; set; } = [];

    /// <summary>
    /// 展平的阅读顺序列表（用于导航），仅含含有 Href 的章节
    /// </summary>
    public List<Chapter> FlatChapters { get; set; } = [];

    /// <summary>
    /// EPUB 内 OPF 所在目录的基础路径（如 OEBPS/），用于拼接资源路径
    /// </summary>
    public string BasePath { get; set; } = string.Empty;
}
