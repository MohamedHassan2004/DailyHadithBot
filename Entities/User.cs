namespace TelegramBot.Entities;

public class User
{
    public int Id { get; set; }
    public long TelegramChatId { get; set; }
    public int CurrentHadithIndex { get; set; } = 1;
    public DateTime JoinedAt { get; set; } = DateTime.UtcNow;
}
