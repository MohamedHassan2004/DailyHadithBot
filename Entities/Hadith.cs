namespace TelegramBot.Entities;

public class Hadith
{
    public int Id { get; set; }
    public string Text { get; set; } = string.Empty;
    public string BookName { get; set; } = string.Empty;
    public int ChapterId { get; set; }
}
