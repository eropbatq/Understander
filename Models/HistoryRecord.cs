using SQLite;

namespace Understander.Models;

public class HistoryRecord
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    public string Source { get; set; } = "";

    public string Result { get; set; } = "";

    public string Pair { get; set; } = "";

    public string Date { get; set; } = "";

    // Helper property to parse date
    public DateTime DateParsed => DateTime.TryParse(Date, out var dt) ? dt : DateTime.MinValue;
}
