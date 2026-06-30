using Microsoft.Data.Sqlite;
using EpubRead.Models;

namespace EpubRead.Services;

/// <summary>
/// 高亮笔记持久化服务 —— 基于 SQLite，按书籍+章节存储选区高亮。
/// </summary>
public class NoteService
{
    private readonly string _connectionString;

    public NoteService(string dbPath)
    {
        _connectionString = $"Data Source={dbPath}";
    }

    /// <summary>
    /// 创建 Notes 表（如不存在）。若检测到旧版表结构（含 StartSelector/EndSelector 列）则删表重建。
    /// </summary>
    public void InitializeTable()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        // 检测旧表结构：若存在 StartSelector 列，说明是旧版（CSS 路径定位），删表重建
        var checkCmd = connection.CreateCommand();
        checkCmd.CommandText = "SELECT COUNT(*) FROM pragma_table_info('Notes') WHERE name = 'StartSelector'";
        var hasOldColumn = Convert.ToInt64(checkCmd.ExecuteScalar()) > 0;
        if (hasOldColumn)
        {
            var dropCmd = connection.CreateCommand();
            dropCmd.CommandText = "DROP TABLE Notes";
            dropCmd.ExecuteNonQuery();
        }

        var cmd = connection.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS Notes (
                Id              TEXT PRIMARY KEY,
                BookId          TEXT NOT NULL,
                ChapterHref     TEXT NOT NULL,
                StartOffset     INTEGER NOT NULL,
                EndOffset       INTEGER NOT NULL,
                SelectedText    TEXT NOT NULL,
                Color           TEXT NOT NULL,
                CreatedAt       TEXT NOT NULL
            )
            """;
        cmd.ExecuteNonQuery();

        // 按书+章查询的索引
        var idxCmd = connection.CreateCommand();
        idxCmd.CommandText = "CREATE INDEX IF NOT EXISTS IX_Notes_Book_Chapter ON Notes(BookId, ChapterHref)";
        idxCmd.ExecuteNonQuery();

        // 迁移：为旧表补充 Style / Comment 列（供下划线样式与后续批注使用）
        EnsureColumn(connection, "Style", "TEXT NOT NULL DEFAULT 'highlight'");
        EnsureColumn(connection, "Comment", "TEXT NOT NULL DEFAULT ''");
    }

    /// <summary>若表缺少指定列则追加（SQLite ALTER TABLE ADD COLUMN），用于平滑升级旧库。</summary>
    private static void EnsureColumn(Microsoft.Data.Sqlite.SqliteConnection connection, string column, string definition)
    {
        var check = connection.CreateCommand();
        check.CommandText = $"SELECT COUNT(*) FROM pragma_table_info('Notes') WHERE name = '{column}'";
        if (Convert.ToInt64(check.ExecuteScalar()) == 0)
        {
            var add = connection.CreateCommand();
            add.CommandText = $"ALTER TABLE Notes ADD COLUMN {column} {definition}";
            add.ExecuteNonQuery();
        }
    }

    /// <summary>
    /// 获取指定书籍指定章节的所有高亮笔记
    /// </summary>
    public List<Note> GetNotes(string bookId, string chapterHref)
    {
        var list = new List<Note>();
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT Id, BookId, ChapterHref, StartOffset, EndOffset, SelectedText, Color, Style, Comment, CreatedAt FROM Notes WHERE BookId = @bookId AND ChapterHref = @href ORDER BY StartOffset ASC";
        cmd.Parameters.AddWithValue("@bookId", bookId);
        cmd.Parameters.AddWithValue("@href", chapterHref);

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new Note
            {
                Id = reader.GetString(0),
                BookId = reader.GetString(1),
                ChapterHref = reader.GetString(2),
                StartOffset = reader.GetInt32(3),
                EndOffset = reader.GetInt32(4),
                SelectedText = reader.GetString(5),
                Color = reader.GetString(6),
                Style = reader.GetString(7),
                Comment = reader.GetString(8),
                CreatedAt = DateTime.Parse(reader.GetString(9))
            });
        }
        return list;
    }

    /// <summary>
    /// 保存一条高亮笔记
    /// </summary>
    public void SaveNote(Note note)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO Notes (Id, BookId, ChapterHref, StartOffset, EndOffset, SelectedText, Color, Style, Comment, CreatedAt)
            VALUES (@id, @bookId, @href, @so, @eo, @text, @color, @style, @comment, @created)
            """;
        cmd.Parameters.AddWithValue("@id", note.Id);
        cmd.Parameters.AddWithValue("@bookId", note.BookId);
        cmd.Parameters.AddWithValue("@href", note.ChapterHref);
        cmd.Parameters.AddWithValue("@so", note.StartOffset);
        cmd.Parameters.AddWithValue("@eo", note.EndOffset);
        cmd.Parameters.AddWithValue("@text", note.SelectedText);
        cmd.Parameters.AddWithValue("@color", note.Color);
        cmd.Parameters.AddWithValue("@style", note.Style);
        cmd.Parameters.AddWithValue("@comment", note.Comment);
        cmd.Parameters.AddWithValue("@created", note.CreatedAt.ToString("O"));
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// 更新指定笔记的批注内容与标注样式（编辑批注时调用）
    /// </summary>
    public void UpdateNote(string noteId, string comment, string color, string style)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        var cmd = connection.CreateCommand();
        cmd.CommandText = "UPDATE Notes SET Comment = @comment, Color = @color, Style = @style WHERE Id = @id";
        cmd.Parameters.AddWithValue("@comment", comment);
        cmd.Parameters.AddWithValue("@color", color);
        cmd.Parameters.AddWithValue("@style", style);
        cmd.Parameters.AddWithValue("@id", noteId);
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// 删除一条高亮笔记
    /// </summary>
    public void DeleteNote(string noteId)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM Notes WHERE Id = @id";
        cmd.Parameters.AddWithValue("@id", noteId);
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// 删除指定书籍的所有高亮笔记（删除书籍时调用）
    /// </summary>
    public void DeleteNotesByBook(string bookId)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM Notes WHERE BookId = @bookId";
        cmd.Parameters.AddWithValue("@bookId", bookId);
        cmd.ExecuteNonQuery();
    }
}
