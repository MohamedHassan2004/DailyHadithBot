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
// 3. DATA SEEDING (FIRST RUN)
// ========================================
if (!await db.Hadiths.AnyAsync())
{
    Console.WriteLine("Database is empty. Fetching Hadith data...");
    
    const string hadithJsonUrl = "https://raw.githubusercontent.com/AhmedBaset/hadith-json/main/db/by_book/other_books/riyad_assalihin.json";
    
    using var httpClient = new HttpClient();
    var jsonContent = await httpClient.GetStringAsync(hadithJsonUrl);
    
    // Parse JSON - the structure contains hadiths and chapters arrays
    var hadithData = JsonConvert.DeserializeObject<HadithJsonRoot>(jsonContent);
    
    if (hadithData?.Hadiths != null && hadithData.Hadiths.Count > 0 && hadithData.Chapters != null)
    {
        // Create a dictionary of chapter IDs to Arabic book names
        var chapterNames = hadithData.Chapters.ToDictionary(c => c.Id, c => c.Arabic ?? string.Empty);
        
        // Order hadiths: "كتاب المقدمات" (chapterId = 0) first, then by chapterId, then by idInBook
        var orderedHadiths = hadithData.Hadiths
            .OrderBy(h => h.ChapterId == 0 ? 0 : 1)  // المقدمات first
            .ThenBy(h => h.ChapterId)
            .ThenBy(h => h.IdInBook)
            .ToList();
        
        var hadiths = orderedHadiths.Select((h, index) => new Hadith
        {
            Id = index + 1,
            Text = h.Arabic ?? string.Empty,
            ChapterId = h.ChapterId,
            BookName = chapterNames.GetValueOrDefault(h.ChapterId, "غير محدد")
        }).ToList();
        
        await db.Hadiths.AddRangeAsync(hadiths);
        await db.SaveChangesAsync();
        
        Console.WriteLine($"Seeded {hadiths.Count} hadiths into the database.");
    }
    else
    {
        Console.WriteLine("Warning: No hadiths found in the JSON data.");
    }
}
else
{
    Console.WriteLine($"Database already contains {await db.Hadiths.CountAsync()} hadiths.");
}

// ========================================
// 4. CATCH-UP LOGIC (HANDLE NEW USERS)
// ========================================
Console.WriteLine("Checking for offline updates...");

try
{
    var updates = await botClient.GetUpdates(offset: 0, limit: 100);
    Console.WriteLine($"Found {updates.Length} pending updates.");

    foreach (var update in updates)
    {
        if (update.Message?.Text?.StartsWith("/start") == true)
        {
            var chatId = update.Message.Chat.Id;
            
            // Check if user already exists
            var existingUser = await db.Users.FirstOrDefaultAsync(u => u.TelegramChatId == chatId);
            
            if (existingUser == null)
            {
                // Add new user
                var newUser = new User
                {
                    TelegramChatId = chatId,
                    CurrentHadithIndex = 1,
                    JoinedAt = DateTime.UtcNow
                };
                
                await db.Users.AddAsync(newUser);
                await db.SaveChangesAsync();
                
                // Send welcome message
                await botClient.SendMessage(
                    chatId: chatId,
                    text: "مرحباً بك! تم تسجيلك بنجاح. ستتلقى حديثاً يومياً من رياض الصالحين. 📖"
                );
                
                Console.WriteLine($"New user registered: {chatId}");
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
// 5. DAILY BROADCAST LOGIC
// ========================================
Console.WriteLine("Starting daily broadcast...");

var totalHadiths = await db.Hadiths.CountAsync();
var users = await db.Users.ToListAsync();

Console.WriteLine($"Broadcasting to {users.Count} users. Total hadiths: {totalHadiths}");

foreach (var user in users)
{
    try
    {
        // Handle cycle completion
        if (user.CurrentHadithIndex > totalHadiths)
        {
            user.CurrentHadithIndex = 1;
            
            await botClient.SendMessage(
                chatId: user.TelegramChatId,
                text: "🎉 مبارك! لقد أكملت دورة كاملة من أحاديث رياض الصالحين. سنبدأ من جديد!"
            );
            
            Console.WriteLine($"User {user.TelegramChatId}: Cycle completed, reset to 1.");
        }
        
        // Get current hadith
        var hadith = await db.Hadiths.FirstOrDefaultAsync(h => h.Id == user.CurrentHadithIndex);
        
        if (hadith != null)
        {
            var messageText = $"📖 حديث اليوم ({user.CurrentHadithIndex}/{totalHadiths})\n📚 من {hadith.BookName}\n\n{hadith.Text}";
            
            await botClient.SendMessage(
                chatId: user.TelegramChatId,
                text: messageText
            );
            
            // Increment index
            user.CurrentHadithIndex++;
            
            Console.WriteLine($"User {user.TelegramChatId}: Sent hadith #{user.CurrentHadithIndex - 1}");
        }
        else
        {
            Console.WriteLine($"User {user.TelegramChatId}: Hadith #{user.CurrentHadithIndex} not found.");
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error sending to user {user.TelegramChatId}: {ex.Message}");
    }
}

// Save all changes
await db.SaveChangesAsync();

Console.WriteLine("Daily broadcast completed. Exiting...");

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
