using backend_dickifier.Entities;
using Microsoft.EntityFrameworkCore;

namespace backend_dickifier.Data;

public class DickifierDbContext : DbContext
{
    public DickifierDbContext(DbContextOptions<DickifierDbContext> options) : base(options)
    {
    }

    public DbSet<User> Users => Set<User>();
    public DbSet<DickModel> Models => Set<DickModel>();
    public DbSet<ModelVote> ModelVotes => Set<ModelVote>();
    public DbSet<RefreshSession> RefreshSessions => Set<RefreshSession>();
    public DbSet<ResourceBudget> ResourceBudgets => Set<ResourceBudget>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Tabla Users
        modelBuilder.Entity<User>(entity =>
        {
            entity.HasIndex(u => u.Username)
                .IsUnique();
            entity.HasIndex(u => u.NormalizedUsername).IsUnique();
        });

        modelBuilder.Entity<RefreshSession>(entity =>
        {
            entity.HasIndex(s => s.TokenHash).IsUnique();
            entity.HasIndex(s => new { s.FamilyId, s.UserId });
            entity.HasIndex(s => s.ExpiresAt);
            entity.HasOne(s => s.User).WithMany().HasForeignKey(s => s.UserId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<ResourceBudget>().HasIndex(b => b.ExpiresAt);

        // Tabla DickModels
        modelBuilder.Entity<DickModel>(entity =>
        {
            entity.HasIndex(m => m.ShortCode)
                .IsUnique();

            entity.HasIndex(m => m.LengthCm);
            entity.HasIndex(m => m.GirthCm);
            entity.HasIndex(m => m.VolumeCm3);
            entity.HasIndex(m => m.Status);
            entity.HasIndex(m => m.CreatedAt);
            entity.HasIndex(m => m.CountryCode);

            entity.HasOne(m => m.User)
                .WithMany(u => u.Models)
                .HasForeignKey(m => m.UserId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // Tabla ModelVotes
        modelBuilder.Entity<ModelVote>(entity =>
        {
            entity.HasIndex(v => new { v.ModelId, v.VoterFingerprintHash })
                .IsUnique();

            entity.HasOne(v => v.Model)
                .WithMany(m => m.Votes)
                .HasForeignKey(v => v.ModelId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
