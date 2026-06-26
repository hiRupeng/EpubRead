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
    /// OPF spine 中的所有 XHTML 文件 href 列表（相对于 BasePath，不含锚点），
    /// 按 spine 阅读顺序排列。用于加载完整章节内容（一个逻辑章节可能跨多个 spine 文件）。
    /// </summary>
    public List<string> SpineFiles { get; set; } = [];

    /// <summary>
    /// EPUB 内 OPF 所在目录的基础路径（如 OEBPS/），用于拼接资源路径
    /// </summary>
    public string BasePath { get; set; } = string.Empty;
}
