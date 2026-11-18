using SQLite;
using System.Diagnostics;
using Understander.Models;

namespace Understander.Services;

public class HistoryDatabase
{
    readonly string _dbPath;
    SQLiteAsyncConnection? _database;

    public HistoryDatabase(string dbPath)
    {
        _dbPath = dbPath;
        Debug.WriteLine($"[HistoryDB] Path: {_dbPath}");
    }

    private SQLiteAsyncConnection GetConnection()
    {
        if (_database != null) return _database;
        
        lock (this)
        {
            if (_database == null)
            {
                _database = new SQLiteAsyncConnection(_dbPath, true);
                Debug.WriteLine($"[HistoryDB] Connection created");
            }
        }
        return _database;
    }

    public async Task InitializeAsync()
    {
        try
        {
            var db = GetConnection();
            await db.CreateTableAsync<HistoryRecord>();
            Debug.WriteLine("[HistoryDB] Table created");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[HistoryDB] Init error: {ex.Message}");
        }
    }

    public async Task<List<HistoryRecord>> GetRecordsAsync()
    {
        try
        {
            var db = GetConnection();
            var records = await db.QueryAsync<HistoryRecord>("SELECT * FROM HistoryRecord ORDER BY Date DESC");
            Debug.WriteLine($"[HistoryDB] Loaded {records.Count} records");
            return records;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[HistoryDB] GetRecords error: {ex.Message}");
            return new List<HistoryRecord>();
        }
    }

    public async Task<int> InsertRecordAsync(HistoryRecord record)
    {
        try
        {
            var db = GetConnection();
            record.Date = DateTime.UtcNow.ToString("o");
            var result = await db.InsertAsync(record);
            Debug.WriteLine($"[HistoryDB] Inserted record: {record.Source[..Math.Min(30, record.Source.Length)]}... -> {record.Result[..Math.Min(30, record.Result.Length)]}");
            return result;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[HistoryDB] Insert error: {ex.Message}");
            return 0;
        }
    }

    public async Task<int> DeleteRecordAsync(HistoryRecord record)
    {
        var db = GetConnection();
        return await db.DeleteAsync(record);
    }

    public async Task<int> ClearAllAsync()
    {
        var db = GetConnection();
        return await db.ExecuteAsync("DELETE FROM HistoryRecord");
    }
}
