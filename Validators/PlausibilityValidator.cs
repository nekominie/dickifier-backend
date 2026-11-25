using System.Text.RegularExpressions;

namespace backend_dickifier.Validators;

public record ValidationResult(bool IsValid, string? ErrorMessage = null)
{
    public static ValidationResult Success() => new(true);
    public static ValidationResult Fail(string error) => new(false, error);
}

public static class PlausibilityValidator
{
    private static readonly HashSet<string> ValidDirections = new(StringComparer.OrdinalIgnoreCase)
    {
        "straight", "up", "down", "left", "right"
    };

    private static readonly string[] OffensivePatterns =
    {
        "nazi", "hitler", "child", "pedophil", "rape", "cp"
    };

    public static ValidationResult Validate(
        decimal lengthCm,
        decimal girthCm,
        string curvatureDirection,
        int curvatureDegrees,
        string name,
        decimal? heightCm = null,
        decimal? weightKg = null,
        string? countryCode = null)
    {
        // 1. Longitud biológicamente plausible (3.0 cm a 32.0 cm)
        if (lengthCm < 3.0m || lengthCm > 32.0m)
        {
            return ValidationResult.Fail($"La longitud ({lengthCm} cm) está fuera del rango biológicamente plausible (3.0 cm a 32.0 cm).");
        }

        // 2. Perímetro / circunferencia biológicamente plausible (3.0 cm a 25.0 cm)
        if (girthCm < 3.0m || girthCm > 25.0m)
        {
            return ValidationResult.Fail($"El perímetro ({girthCm} cm) está fuera del rango biológicamente plausible (3.0 cm a 25.0 cm).");
        }

        // 3. Dirección de curvatura
        if (!ValidDirections.Contains(curvatureDirection))
        {
            return ValidationResult.Fail($"Dirección de curvatura inválida: '{curvatureDirection}'. Valores válidos: straight, up, down, left, right.");
        }

        // 4. Grados de curvatura (0 a 60)
        if (curvatureDegrees < 0 || curvatureDegrees > 60)
        {
            return ValidationResult.Fail($"Los grados de curvatura ({curvatureDegrees}°) deben estar entre 0° y 60°.");
        }

        if (string.Equals(curvatureDirection, "straight", StringComparison.OrdinalIgnoreCase) && curvatureDegrees != 0)
        {
            // Opcionalmente tolerante o normalizable a 0
        }

        // 5. Estatura demográfica opcional (100 cm a 250 cm)
        if (heightCm.HasValue && (heightCm.Value < 100.0m || heightCm.Value > 250.0m))
        {
            return ValidationResult.Fail($"La estatura demográfica ({heightCm.Value} cm) debe estar entre 100.0 cm y 250.0 cm.");
        }

        // 6. Peso demográfico opcional (35 kg a 280 kg)
        if (weightKg.HasValue && (weightKg.Value < 35.0m || weightKg.Value > 280.0m))
        {
            return ValidationResult.Fail($"El peso demográfico ({weightKg.Value} kg) debe estar entre 35.0 kg y 280.0 kg.");
        }

        // 7. Código de país opcional (ISO Alpha-2: 2 letras)
        if (!string.IsNullOrWhiteSpace(countryCode))
        {
            var cleaned = countryCode.Trim();
            if (cleaned.Length != 2 || !char.IsLetter(cleaned[0]) || !char.IsLetter(cleaned[1]))
            {
                return ValidationResult.Fail($"El código de país '{countryCode}' debe ser un código ISO Alpha-2 de 2 letras (ej. 'MX', 'ES').");
            }
        }

        // 8. Nombre del modelo y filtro anti-trolls
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length < 2)
        {
            return ValidationResult.Fail("El nombre del modelo debe tener al menos 2 caracteres.");
        }

        var trimmedName = name.Trim();
        if (trimmedName.Length > 80)
        {
            return ValidationResult.Fail("El nombre del modelo no puede exceder los 80 caracteres.");
        }

        // Rechazar caracteres de control ASCII
        if (trimmedName.Any(char.IsControl))
        {
            return ValidationResult.Fail("El nombre contiene caracteres de control no permitidos.");
        }

        // Filtro anti-trolls
        var lower = trimmedName.ToLowerInvariant();
        foreach (var pattern in OffensivePatterns)
        {
            if (lower.Contains(pattern))
            {
                return ValidationResult.Fail("El nombre del modelo contiene términos ofensivos o no permitidos.");
            }
        }

        return ValidationResult.Success();
    }
}
