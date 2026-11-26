using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace backend_dickifier.Entities;

[Table("refresh_sessions")]
public sealed class RefreshSession
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid FamilyId { get; set; }
    public Guid UserId { get; set; }
    public User User { get; set; } = null!;
    [MaxLength(64)] public string TokenHash { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? RevokedAt { get; set; }
}
