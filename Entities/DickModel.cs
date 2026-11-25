using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace backend_dickifier.Entities;

[Table("dick_models")]
public class DickModel
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    [MaxLength(12)]
    public string ShortCode { get; set; } = string.Empty;

    public Guid? UserId { get; set; }

    [ForeignKey(nameof(UserId))]
    public virtual User? User { get; set; }

    [MaxLength(64)]
    public string? EditTokenHash { get; set; }

    [Required]
    [MaxLength(80)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [MaxLength(20)]
    public string Status { get; set; } = "anonymous"; // 'anonymous', 'registered', 'flagged'

    // Métricas fisiológicas indexadas
    [Column(TypeName = "decimal(4, 1)")]
    public decimal LengthCm { get; set; }

    [Column(TypeName = "decimal(4, 1)")]
    public decimal GirthCm { get; set; }

    [Column(TypeName = "decimal(6, 1)")]
    public decimal VolumeCm3 { get; set; }

    [Required]
    [MaxLength(20)]
    public string CurvatureDirection { get; set; } = "straight"; // 'straight', 'up', 'down', 'left', 'right'

    public int CurvatureDegrees { get; set; } = 0;

    // Datos demográficos y de investigación (opcionales)
    [MaxLength(2)]
    public string? CountryCode { get; set; }

    [Column(TypeName = "decimal(4, 1)")]
    public decimal? HeightCm { get; set; }

    [Column(TypeName = "decimal(4, 1)")]
    public decimal? WeightKg { get; set; }

    // Geometría 3D completa en JSONB
    [Column(TypeName = "jsonb")]
    public string AnatomyPayload { get; set; } = "{}";

    // Social / Metadatos
    public int UpvotesCount { get; set; } = 0;

    public int ViewsCount { get; set; } = 0;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public virtual ICollection<ModelVote> Votes { get; set; } = new List<ModelVote>();
}
