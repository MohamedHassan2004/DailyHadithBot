using Microsoft.EntityFrameworkCore;
using TelegramBot.Entities;

namespace TelegramBot.Data;

public class AppDbContext : DbContext
{
    public DbSet<User> Users { get; set; }
    public DbSet<Hadith> Hadiths { get; set; }

    private readonly string _connectionString;

    public AppDbContext(string connectionString)
    {
        _connectionString = connectionString;
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.UseNpgsql(_connectionString);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(u => u.Id);
            entity.HasIndex(u => u.TelegramChatId).IsUnique();
        });

        modelBuilder.Entity<Hadith>(entity =>
        {
            entity.HasKey(h => h.Id);
        });
    }
}
