using System.IO;
using Microsoft.Data.Sqlite;
using EpubRead.Models;

namespace EpubRead.Services;

/// <summary>
/// 书架数据持久化服务 —— 基于 SQLite，管理书籍的增删查
/// </summary>
public class BookshelfService
{
    private readonly string _connectionString;

    public BookshelfService(string dbPath)
    {
        _connectionString = $"Data Source={dbPath}";
    }

    /// <summary>
    /// 初始化数据库，创建 Books 表（如不存在）并进行版本迁移
    /// </summary>
    public void InitializeDatabase()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        var cmd = connection.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS Books (
                Id          TEXT PRIMARY KEY,
                Title       TEXT NOT NULL,
                Author      TEXT NOT NULL,
                FilePath    TEXT NOT NULL,
                CoverPath   TEXT,
                ImportDate  TEXT NOT NULL
            )
            """;
        cmd.ExecuteNonQuery();

        // 迁移：添加新字段（如已存在则忽略）
        MigrateAddColumn(connection, "Books", "LastReadChapterIndex", "INTEGER DEFAULT -1");
        MigrateAddColumn(connection, "Books", "TotalChapters", "INTEGER DEFAULT 0");
    }

    private static void MigrateAddColumn(SqliteConnection connection, string table, string column, string columnDef)
    {
        try
        {
            var cmd = connection.CreateCommand();
            cmd.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {columnDef}";
            cmd.ExecuteNonQuery();
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 1 && ex.Message.Contains("duplicate column"))
        {
            // 列已存在，忽略
        }
    }

    /// <summary>
    /// 获取所有书籍，按导入时间倒序
    /// </summary>
    public List<Book> GetAllBooks()
    {
        var books = new List<Book>();
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT Id, Title, Author, FilePath, CoverPath, ImportDate, LastReadChapterIndex, TotalChapters FROM Books ORDER BY ImportDate DESC";

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            books.Add(new Book
            {
                Id = reader.GetString(0),
                Title = reader.GetString(1),
                Author = reader.GetString(2),
                FilePath = reader.GetString(3),
                CoverPath = reader.IsDBNull(4) ? null : reader.GetString(4),
                ImportDate = DateTime.Parse(reader.GetString(5)),
                LastReadChapterIndex = reader.IsDBNull(6) ? -1 : reader.GetInt32(6),
                TotalChapters = reader.IsDBNull(7) ? 0 : reader.GetInt32(7)
            });
        }

        return books;
    }

    /// <summary>
    /// 添加书籍到书架
    /// </summary>
    public void AddBook(Book book)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO Books (Id, Title, Author, FilePath, CoverPath, ImportDate, LastReadChapterIndex, TotalChapters)
            VALUES (@id, @title, @author, @filePath, @coverPath, @importDate, @lastReadChapterIndex, @totalChapters)
            """;
        cmd.Parameters.AddWithValue("@id", book.Id);
        cmd.Parameters.AddWithValue("@title", book.Title);
        cmd.Parameters.AddWithValue("@author", book.Author);
        cmd.Parameters.AddWithValue("@filePath", book.FilePath);
        cmd.Parameters.AddWithValue("@coverPath", (object?)book.CoverPath ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@importDate", book.ImportDate.ToString("O"));
        cmd.Parameters.AddWithValue("@lastReadChapterIndex", book.LastReadChapterIndex);
        cmd.Parameters.AddWithValue("@totalChapters", book.TotalChapters);
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// 从书架移除书籍（数据库 + 封面缓存文件）
    /// </summary>
    public void RemoveBook(string bookId, string coversDir)
    {
        // 先查出封面路径以便清理文件
        string? coverPath = null;
        using (var connection = new SqliteConnection(_connectionString))
        {
            connection.Open();
            var selectCmd = connection.CreateCommand();
            selectCmd.CommandText = "SELECT CoverPath FROM Books WHERE Id = @id";
            selectCmd.Parameters.AddWithValue("@id", bookId);
            var result = selectCmd.ExecuteScalar();
            if (result != DBNull.Value && result != null)
                coverPath = result.ToString();
        }

        // 从数据库删除
        using (var connection = new SqliteConnection(_connectionString))
        {
            connection.Open();
            var deleteCmd = connection.CreateCommand();
            deleteCmd.CommandText = "DELETE FROM Books WHERE Id = @id";
            deleteCmd.Parameters.AddWithValue("@id", bookId);
            deleteCmd.ExecuteNonQuery();
        }

        // 清理封面缓存
        if (!string.IsNullOrEmpty(coverPath) && File.Exists(coverPath))
        {
            try { File.Delete(coverPath); }
            catch { /* 忽略清理失败 */ }
        }
    }

    /// <summary>
    /// 更新书籍阅读进度
    /// </summary>
    public void UpdateBookProgress(string bookId, int chapterIndex, int totalChapters)
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        var cmd = connection.CreateCommand();
        cmd.CommandText = """
            UPDATE Books SET LastReadChapterIndex = @chapterIndex, TotalChapters = @totalChapters
            WHERE Id = @id
            """;
        cmd.Parameters.AddWithValue("@id", bookId);
        cmd.Parameters.AddWithValue("@chapterIndex", chapterIndex);
        cmd.Parameters.AddWithValue("@totalChapters", totalChapters);
        cmd.ExecuteNonQuery();
    }
}
