using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace backend_dickifier.Entities;

[Table("resource_budgets")]
public sealed class ResourceBudget
{
    [Key, MaxLength(180)] public string Key { get; set; } = "";
    public int Count { get; set; }
    public long Bytes { get; set; }
    public DateTime? ExpiresAt { get; set; }
}
