using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace backend_dickifier.Entities;

[Table("users")]
public class User
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    [MaxLength(50)]
    public string Username { get; set; } = string.Empty;
    [Required, MaxLength(50)]
    public string NormalizedUsername { get; set; } = string.Empty;

    [Required]
    public string PasswordHash { get; set; } = string.Empty;

    [MaxLength(255)]
    public string? Email { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Column(TypeName = "jsonb")]
    public string? WorkspaceDraftJson { get; set; }
    public int WorkspaceBytes { get; set; }

    public DateTime? WorkspaceDraftUpdatedAt { get; set; }

    public virtual ICollection<DickModel> Models { get; set; } = new List<DickModel>();
}
