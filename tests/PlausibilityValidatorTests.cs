using backend_dickifier.Validators;
using Xunit;

namespace backend_dickifier.Tests;

public class PlausibilityValidatorTests
{
    [Fact]
    public void Validate_ValidParameters_ReturnsSuccess()
    {
        var result = PlausibilityValidator.Validate(
            lengthCm: 14.5m,
            girthCm: 11.8m,
            curvatureDirection: "straight",
            curvatureDegrees: 0,
            name: "El Obelisco",
            heightCm: 178.0m,
            weightKg: 75.5m,
            countryCode: "ES"
        );

        Assert.True(result.IsValid);
        Assert.Null(result.ErrorMessage);
    }

    [Theory]
    [InlineData(2.9)]
    [InlineData(0.0)]
    [InlineData(-5.0)]
    [InlineData(32.1)]
    [InlineData(50.0)]
    public void Validate_InvalidLength_Fails(decimal invalidLength)
    {
        var result = PlausibilityValidator.Validate(
            lengthCm: invalidLength,
            girthCm: 11.5m,
            curvatureDirection: "straight",
            curvatureDegrees: 0,
            name: "Test"
        );

        Assert.False(result.IsValid);
        Assert.Contains("longitud", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(2.9)]
    [InlineData(0.0)]
    [InlineData(25.1)]
    [InlineData(40.0)]
    public void Validate_InvalidGirth_Fails(decimal invalidGirth)
    {
        var result = PlausibilityValidator.Validate(
            lengthCm: 14.0m,
            girthCm: invalidGirth,
            curvatureDirection: "straight",
            curvatureDegrees: 0,
            name: "Test"
        );

        Assert.False(result.IsValid);
        Assert.Contains("perímetro", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(99.0)]
    [InlineData(251.0)]
    public void Validate_InvalidHeight_Fails(decimal invalidHeight)
    {
        var result = PlausibilityValidator.Validate(
            lengthCm: 14.0m,
            girthCm: 11.5m,
            curvatureDirection: "straight",
            curvatureDegrees: 0,
            name: "Test",
            heightCm: invalidHeight
        );

        Assert.False(result.IsValid);
        Assert.Contains("estatura", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(34.0)]
    [InlineData(281.0)]
    public void Validate_InvalidWeight_Fails(decimal invalidWeight)
    {
        var result = PlausibilityValidator.Validate(
            lengthCm: 14.0m,
            girthCm: 11.5m,
            curvatureDirection: "straight",
            curvatureDegrees: 0,
            name: "Test",
            weightKg: invalidWeight
        );

        Assert.False(result.IsValid);
        Assert.Contains("peso", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_ControlCharactersInName_Fails()
    {
        var result = PlausibilityValidator.Validate(
            lengthCm: 14.0m,
            girthCm: 11.5m,
            curvatureDirection: "straight",
            curvatureDegrees: 0,
            name: "Modelo\u0000Invalido"
        );

        Assert.False(result.IsValid);
        Assert.Contains("control", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_OffensiveTrollName_Fails()
    {
        var result = PlausibilityValidator.Validate(
            lengthCm: 14.0m,
            girthCm: 11.5m,
            curvatureDirection: "straight",
            curvatureDegrees: 0,
            name: "Hitler_Super_Model"
        );

        Assert.False(result.IsValid);
        Assert.Contains("ofensivos", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("invalid_dir")]
    [InlineData("diagonal")]
    public void Validate_InvalidCurvatureDirection_Fails(string dir)
    {
        var result = PlausibilityValidator.Validate(
            lengthCm: 14.0m,
            girthCm: 11.5m,
            curvatureDirection: dir,
            curvatureDegrees: 0,
            name: "Modelo Curvo"
        );

        Assert.False(result.IsValid);
        Assert.Contains("curvatura", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }
}
