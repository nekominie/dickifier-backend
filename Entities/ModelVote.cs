using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace backend_dickifier.Entities;

[Table("model_votes")]
public class ModelVote
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    public Guid ModelId { get; set; }

    [ForeignKey(nameof(ModelId))]
    public virtual DickModel? Model { get; set; }

    [Required]
    [MaxLength(64)]
    public string VoterFingerprintHash { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
