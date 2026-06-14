using System.IO;
using System.IO.Compression;
using System.Xml.Linq;
using EpubRead.Models;

namespace EpubRead.Services;

/// <summary>
/// EPUB 文件解析服务 —— 负责解析 ZIP 封装的 EPUB，提取元数据、封面和章节结构
/// </summary>
public class EpubParser
{
    /// <summary>
    /// 解析 EPUB 文件，返回 EpubBook 对象
    /// </summary>
    public EpubBook Parse(string epubFilePath, Stream? coverOutputStream = null)
    {
        if (!File.Exists(epubFilePath))
            throw new FileNotFoundException("EPUB 文件不存在", epubFilePath);

        using var archive = ZipFile.OpenRead(epubFilePath);

        // 1. 解析 container.xml → 获取 OPF 路径
        var opfPath = GetOpfPath(archive);

        // 2. 切割基础路径（OPF 所在目录）
        var basePath = Path.GetDirectoryName(opfPath)?.Replace('\\', '/') ?? string.Empty;
        if (basePath.Length > 0 && !basePath.EndsWith('/'))
            basePath += '/';

        // 3. 解析 OPF → 元数据、manifest、spine
        var (title, author, coverId, manifest, spine) = ParseOpf(archive, opfPath);

        // 4. 解析目录（NCX / NAV）
        var chapters = ParseNav(archive, basePath, manifest, spine);

        // 5. 提取封面图片
        byte[]? coverImage = null;
        if (coverId != null && manifest.TryGetValue(coverId, out var coverHref))
        {
            coverImage = ReadEntryBytes(archive, basePath + coverHref);
        }

        // 如果提供了输出流，写入封面（用于缓存到本地）
        if (coverOutputStream != null && coverImage != null)
        {
            coverOutputStream.Write(coverImage, 0, coverImage.Length);
        }

        return new EpubBook
        {
            Title = title,
            Author = author,
            CoverImage = coverImage,
            Chapters = chapters,
            BasePath = basePath
        };
    }

    /// <summary>
    /// 按需加载指定章节的 HTML 内容
    /// </summary>
    public string LoadChapterContent(string epubFilePath, string basePath, string href)
    {
        using var archive = ZipFile.OpenRead(epubFilePath);
        var fullPath = basePath + href;
        var entry = archive.GetEntry(fullPath);
        if (entry == null) return "<p>内容加载失败</p>";

        using var stream = entry.Open();
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    // ─── 内部解析步骤 ───

    private static string GetOpfPath(ZipArchive archive)
    {
        var containerEntry = archive.GetEntry("META-INF/container.xml")
            ?? throw new InvalidDataException("无效的 EPUB：缺少 META-INF/container.xml");

        using var stream = containerEntry.Open();
        var doc = XDocument.Load(stream);
        XNamespace ns = "urn:oasis:names:tc:opendocument:xmlns:container";
        var rootfile = doc.Descendants(ns + "rootfile").FirstOrDefault()
            ?? throw new InvalidDataException("container.xml 中未找到 rootfile 元素");

        return rootfile.Attribute("full-path")?.Value
            ?? throw new InvalidDataException("rootfile 缺少 full-path 属性");
    }

    private static (string title, string author, string? coverId,
        Dictionary<string, string> manifest, List<string> spine)
        ParseOpf(ZipArchive archive, string opfPath)
    {
        var entry = archive.GetEntry(opfPath)
            ?? throw new InvalidDataException($"找不到 OPF 文件：{opfPath}");

        using var stream = entry.Open();
        var doc = XDocument.Load(stream);
        XNamespace dc = "http://purl.org/dc/elements/1.1/";
        XNamespace opf = "http://www.idpf.org/2007/opf";

        var root = doc.Root!;

        // 元数据
        var title = root.Descendants(dc + "title").FirstOrDefault()?.Value.Trim() ?? "未知书名";
        var author = root.Descendants(dc + "creator").FirstOrDefault()?.Value.Trim() ?? "未知作者";

        // Manifest：id → href
        var manifest = root.Element(opf + "manifest")!;
        var items = new Dictionary<string, string>();
        string? coverId = null;

        foreach (var item in manifest.Elements(opf + "item"))
        {
            var id = item.Attribute("id")?.Value;
            var href = item.Attribute("href")?.Value;
            var properties = item.Attribute("properties")?.Value;

            if (id != null && href != null)
            {
                items[id] = href;
                if (properties == "cover-image")
                    coverId = id;
            }
        }

        // 如果没通过 properties 找到封面，尝试通过 meta 标签
        if (coverId == null)
        {
            var metaCover = root.Descendants(opf + "meta")
                .FirstOrDefault(m => m.Attribute("name")?.Value == "cover");
            if (metaCover != null)
                coverId = metaCover.Attribute("content")?.Value;
        }

        // Spine：阅读顺序
        var spineElement = root.Element(opf + "spine")!;
        var spine = spineElement.Elements(opf + "itemref")
            .Select(itemref => itemref.Attribute("idref")?.Value)
            .Where(id => id != null)
            .Select(id => id!)
            .ToList();

        return (title, author, coverId, items, spine);
    }

    private static List<Chapter> ParseNav(ZipArchive archive, string basePath,
        Dictionary<string, string> manifest, List<string> spine)
    {
        var chapters = new List<Chapter>();

        // 1. 尝试解析 NCX 目录
        var ncxHref = manifest.Values
            .FirstOrDefault(v => v.EndsWith(".ncx", StringComparison.OrdinalIgnoreCase));
        if (ncxHref != null)
        {
            chapters = ParseNcx(archive, basePath + ncxHref, basePath);
        }

        // 2. 如果 NCX 无结果，尝试解析 EPUB 3 HTML NAV 目录
        if (chapters.Count == 0)
        {
            chapters = ParseNavHtml(archive, basePath, manifest);
        }

        // 3. 如果都无结果，用 spine 顺序作为 fallback
        if (chapters.Count == 0)
        {
            for (int i = 0; i < spine.Count; i++)
            {
                var id = spine[i];
                if (manifest.TryGetValue(id, out var href))
                {
                    chapters.Add(new Chapter
                    {
                        Title = $"第 {i + 1} 章",
                        Href = href,
                        Order = i
                    });
                }
            }
        }

        return chapters;
    }

    private static List<Chapter> ParseNcx(ZipArchive archive, string ncxPath, string basePath)
    {
        var chapters = new List<Chapter>();
        var entry = archive.GetEntry(ncxPath);
        if (entry == null) return chapters;

        using var stream = entry.Open();
        var doc = XDocument.Load(stream);
        XNamespace ncx = "http://www.daisy.org/z3986/2005/ncx/";

        var navPoints = doc.Descendants(ncx + "navPoint")
            .OrderBy(np => int.Parse(np.Attribute("playOrder")?.Value ?? "0"));

        int order = 0;
        foreach (var navPoint in navPoints)
        {
            var label = navPoint.Element(ncx + "navLabel")?.Element(ncx + "text")?.Value?.Trim();
            var src = navPoint.Element(ncx + "content")?.Attribute("src")?.Value;

            if (!string.IsNullOrWhiteSpace(label) && !string.IsNullOrWhiteSpace(src))
            {
                // src 可能是相对于 OEBPS 的路径，去掉前面的 basePath 部分
                var href = src;
                if (href.StartsWith(basePath, StringComparison.OrdinalIgnoreCase))
                    href = href[basePath.Length..];

                // 分离 #fragment 锚点
                string? anchor = null;
                var hashIndex = href.IndexOf('#');
                if (hashIndex >= 0)
                {
                    anchor = href[(hashIndex + 1)..];
                    href = href[..hashIndex];
                }

                chapters.Add(new Chapter
                {
                    Title = label,
                    Href = href,
                    Anchor = anchor,
                    Order = order++
                });
            }
        }

        return chapters;
    }

    /// <summary>
    /// 解析 EPUB 3 HTML NAV 目录（nav.xhtml 中的 &lt;nav epub:type="toc"&gt;）
    /// </summary>
    private static List<Chapter> ParseNavHtml(ZipArchive archive, string basePath,
        Dictionary<string, string> manifest)
    {
        var chapters = new List<Chapter>();

        // 查找 nav.xhtml 文件
        var navHref = manifest.Values
            .FirstOrDefault(v => v.EndsWith("nav.xhtml", StringComparison.OrdinalIgnoreCase)
                              || v.EndsWith("nav.html", StringComparison.OrdinalIgnoreCase));
        if (navHref == null) return chapters;

        var entry = archive.GetEntry(basePath + navHref);
        if (entry == null) return chapters;

        try
        {
            using var stream = entry.Open();
            using var reader = new StreamReader(stream);
            var html = reader.ReadToEnd();

            // 使用 XHTML 解析
            var doc = XDocument.Parse(html);
            XNamespace xhtml = "http://www.w3.org/1999/xhtml";

            // 查找 epub:type="toc" 的 nav 元素
            var navElement = doc.Descendants(xhtml + "nav")
                .FirstOrDefault(n => n.Attribute("epub:type")?.Value == "toc"
                                  || n.Attribute("type")?.Value == "toc");

            if (navElement == null) return chapters;

            // 提取所有 li > a
            int order = 0;
            foreach (var link in navElement.Descendants(xhtml + "a"))
            {
                var label = link.Value?.Trim();
                var href = link.Attribute("href")?.Value;

                if (!string.IsNullOrWhiteSpace(label) && !string.IsNullOrWhiteSpace(href))
                {
                    // 如果 href 相对于 nav.xhtml 位置，调整
                    if (!href.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                    {
                        var navDir = Path.GetDirectoryName(navHref)?.Replace('\\', '/');
                        if (!string.IsNullOrEmpty(navDir))
                            navDir += '/';
                        else
                            navDir = string.Empty;

                        href = navDir + href;
                    }

                    // 分离 #fragment
                    string? anchor = null;
                    var hashIndex = href.IndexOf('#');
                    if (hashIndex >= 0)
                    {
                        anchor = href[(hashIndex + 1)..];
                        href = href[..hashIndex];
                    }

                    // 去掉 basePath 前缀
                    if (href.StartsWith(basePath, StringComparison.OrdinalIgnoreCase))
                        href = href[basePath.Length..];

                    // 去重：跳过与已有章节相同 href 的条目
                    if (chapters.Any(c => c.Href == href && c.Anchor == anchor))
                        continue;

                    chapters.Add(new Chapter
                    {
                        Title = label,
                        Href = href,
                        Anchor = anchor,
                        Order = order++
                    });
                }
            }
        }
        catch
        {
            // HTML 解析失败，忽略
        }

        return chapters;
    }

    private static byte[]? ReadEntryBytes(ZipArchive archive, string entryPath)
    {
        var entry = archive.GetEntry(entryPath);
        if (entry == null) return null;
        using var stream = entry.Open();
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }
}
