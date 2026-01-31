using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using TelegramBot.Data;
using TelegramBot.Entities;
using User = TelegramBot.Entities.User;

// ========================================
// 1. CONFIGURATION
// ========================================
var builder = new ConfigurationBuilder()
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true) 
    .AddEnvironmentVariables();

IConfiguration config = builder.Build();

// Retrieve and validata configuration
string? connectionString = config.GetConnectionString("DefaultConnection") 
                          ?? Environment.GetEnvironmentVariable("CONNECTION_STRING"); 
string? botToken = config["BotToken"] 
                  ?? Environment.GetEnvironmentVariable("BOT_TOKEN"); 

if (string.IsNullOrEmpty(connectionString))
{
    throw new InvalidOperationException("CRITICAL ERROR: ConnectionString is missing. " +
        "Ensure 'ConnectionStrings:DefaultConnection' is set in appsettings.json OR 'CONNECTION_STRING' environment variable is set.");
}

if (string.IsNullOrEmpty(botToken))
{
    throw new InvalidOperationException("CRITICAL ERROR: BotToken is missing. " +
        "Ensure 'BotToken' is set in appsettings.json OR 'BOT_TOKEN' environment variable is set.");
} 

// ========================================
// 2. INITIALIZE BOT AND DATABASE
// ========================================
var botClient = new TelegramBotClient(botToken);
await using var db = new AppDbContext(connectionString);

// Ensure database is created
await db.Database.EnsureCreatedAsync();
Console.WriteLine("Database connection established.");

// ========================================
// 3. CATCH-UP LOGIC (HANDLE NEW USERS)
// ========================================
Console.WriteLine("Checking for offline updates...");

try
{
    var updates = await botClient.GetUpdates(offset: 0, limit: 100);
    Console.WriteLine($"Found {updates.Length} pending updates.");

    foreach (var update in updates)
    {
        // print update info
        var message = update.Message;
        Console.WriteLine($"UserId: {message?.Chat.Id}, User: {message?.Chat.FirstName} {message?.Chat.LastName}, Message: {message?.Text}");

        if (update.Message?.Text?.StartsWith("/start") == true)
        {
            var chat = update.Message.Chat;
            
            // Check if user already exists
            var existingUser = await db.Users.FirstOrDefaultAsync(u => u.TelegramChatId == chat.Id);
            
            if (existingUser == null)
            {
                // Add new user
                var newUser = new User
                {
                    TelegramChatId = chat.Id,
                    FullName = $"{chat.FirstName} {chat.LastName}" ?? chat.Username ?? string.Empty,
                    CurrentHadithIndex = 1,
                    JoinedAt = DateTime.UtcNow
                };
                
                await db.Users.AddAsync(newUser);
                await db.SaveChangesAsync();
                
                // Send welcome message
                await botClient.SendMessage(
                    chatId: chat.Id,
                    text: "مرحباً بك! تم تسجيلك بنجاح. ستتلقى حديثاً يومياً من رياض الصالحين. 📖"
                );
                
                Console.WriteLine($"New user registered: {chat.Id}");
            }
        }
    }
    
    // Clear processed updates by getting the next offset
    if (updates.Length > 0)
    {
        var lastUpdateId = updates[^1].Id;
        await botClient.GetUpdates(offset: lastUpdateId + 1, limit: 1);
        Console.WriteLine("Cleared processed updates.");
    }
}
catch (Exception ex)
{
    Console.WriteLine($"Error processing updates: {ex.Message}");
}

// ========================================
// 5. Broadcast specific message to all users
// ========================================
Console.WriteLine("Broadcasting specific message to all users...");

// قراءة الرسالة من environment variable أو استخدام رسالة افتراضية
string broadcastMessage = Environment.GetEnvironmentVariable("BROADCAST_MESSAGE") 
    ?? "هذا اختبار لإرسال رسالة محددة إلى جميع المستخدمين.";

var users = await db.Users.ToListAsync();
foreach (var user in users)
{
    try
    {
        await botClient.SendMessage(
            chatId: user.TelegramChatId,
            text: broadcastMessage
        );
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error sending to user {user.FullName}: {ex.Message}");
    }
}
Console.WriteLine("Complete broadcasting message.");

// ========================================
// HELPER CLASSES FOR JSON PARSING
// ========================================
public class HadithJsonRoot
{
    [JsonProperty("hadiths")]
    public List<HadithJson>? Hadiths { get; set; }
    
    [JsonProperty("chapters")]
    public List<ChapterJson>? Chapters { get; set; }
}

public class ChapterJson
{
    [JsonProperty("id")]
    public int Id { get; set; }
    
    [JsonProperty("arabic")]
    public string? Arabic { get; set; }
}

public class HadithJson
{
    [JsonProperty("arabic")]
    public string? Arabic { get; set; }
    
    [JsonProperty("chapterId")]
    public int ChapterId { get; set; }
    
    [JsonProperty("idInBook")]
    public int IdInBook { get; set; }
}
