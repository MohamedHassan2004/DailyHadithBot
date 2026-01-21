namespace TelegramBot.Entities;

public class User
{
    public int Id { get; set; }
    public long TelegramChatId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public int CurrentHadithIndex { get; set; } = 1;
    public DateTime JoinedAt { get; set; } = DateTime.UtcNow;
}
