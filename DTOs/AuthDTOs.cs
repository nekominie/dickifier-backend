using System.ComponentModel.DataAnnotations;
using System.Text.Json;

namespace backend_dickifier.DTOs;

public record RegisterRequest(
    [Required]
    [MinLength(3, ErrorMessage = "El nombre de usuario debe tener al menos 3 caracteres.")]
    [MaxLength(50, ErrorMessage = "El nombre de usuario no puede exceder 50 caracteres.")]
    [RegularExpression(@"^[a-zA-Z0-9_\-\.]+$", ErrorMessage = "El nombre de usuario solo puede contener letras, números, guiones y puntos.")]
    string Username,

    [Required]
    [MinLength(12, ErrorMessage = "La contraseña debe tener al menos 12 caracteres.")]
    [MaxLength(128)]
    string Password,

    [EmailAddress(ErrorMessage = "Formato de correo electrónico inválido.")]
    [MaxLength(255)]
    string? Email = null
);

public record LoginRequest(
    [Required, MaxLength(50)] string Username,
    [Required, MaxLength(128)] string Password
);

public record UserPublicDto(
    Guid Id,
    string Username,
    string? Email,
    DateTime CreatedAt
);

public record AuthResponse(
    string Token,
    UserPublicDto User,
    DateTime ExpiresAt
);

public record SaveWorkspaceDraftRequest(
    [Required] JsonElement ModelPayload,
    DateTime? UpdatedAt = null
);

public record UserProfileResponse(
    UserPublicDto User,
    List<ModelSummaryResponse> Models,
    JsonElement? WorkspaceDraft = null,
    DateTime? WorkspaceDraftUpdatedAt = null
);
