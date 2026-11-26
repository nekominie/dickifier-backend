using System.Buffers;
using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace backend_dickifier.Security;

public sealed class PayloadValidator
{
    private static readonly Dictionary<string, Dictionary<string, (double Min, double Max)>> Ranges = new()
    {
        ["dimensions"] = new() { ["length"] = (3,32), ["girth"] = (3,25), ["taperTop"] = (-1,1), ["taperBottom"] = (-1,1), ["taperTopHeight"] = (.01,1), ["taperBottomHeight"] = (.01,1) },
        ["curvature"] = new() { ["degrees"] = (0,60), ["erectionAngle"] = (45,135), ["curveOrigin"] = (.03,.85) },
        ["glans"] = new() { ["length"] = (.08,.3), ["width"] = (.7,1.5), ["widthX"] = (.7,1.5), ["widthY"] = (.7,1.5), ["coronaFlare"] = (0,1), ["coronaSlope"] = (0,1), ["frenulum"] = (0,1), ["urethraRidge"] = (0,1), ["tilt"] = (-30,30), ["urethraSpread"] = (0,1), ["urethraWidth"] = (0,1), ["urethraDepth"] = (0,1) },
        ["foreskin"] = new() { ["coverage"] = (0,1) },
        ["skin"] = new() { ["roughness"] = (0,1), ["organicNoise"] = (0,1), ["veinIntensity"] = (0,1), ["veinBlur"] = (0,1), ["veinMapBlur"] = (0,8) },
        ["bodyProfile"] = new() { ["heightCm"] = (100,250), ["weightKg"] = (25,350) }
    };
    private static readonly HashSet<string> Metadata = ["id", "name", "shortCode", "createdAt", "updatedAt"];
    public bool TryValidate(JsonElement? input, bool workspace, out string json, out string? error)
    {
        json = "{}"; error = null;
        if (input is null || input.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return workspace ? Fail(out error) : true;
        var root = input.Value;
        if (root.ValueKind != JsonValueKind.Object) return Fail(out error);
        json = root.GetRawText();
        if (Encoding.UTF8.GetByteCount(json) > SecurityOptions.MaxPayloadBytes) return Fail(out error);
        var keys = new HashSet<string>();
        foreach (var section in root.EnumerateObject())
        {
            if (!keys.Add(section.Name)) return Fail(out error);
            if (Metadata.Contains(section.Name))
            {
                if (section.Value.ValueKind != JsonValueKind.String) return Fail(out error);
                var text = section.Value.GetString()!;
                if (text.Length > (section.Name == "name" ? 80 : 64) || text.Any(char.IsControl)) return Fail(out error);
                if (section.Name == "id" && !Guid.TryParse(text, out _)) return Fail(out error);
                if (section.Name is "createdAt" or "updatedAt" && !DateTimeOffset.TryParse(text, out _)) return Fail(out error);
                continue;
            }
            if (!Ranges.TryGetValue(section.Name, out var ranges) || section.Value.ValueKind != JsonValueKind.Object) return Fail(out error);
            var properties = new HashSet<string>();
            foreach (var field in section.Value.EnumerateObject())
            {
                if (!properties.Add(field.Name)) return Fail(out error);
                if (ranges.TryGetValue(field.Name, out var range))
                {
                    if (section.Name == "bodyProfile" && field.Value.ValueKind == JsonValueKind.Null) continue;
                    if (!field.Value.TryGetDoubleSafe(out var number) || !double.IsFinite(number) || number < range.Min || number > range.Max) return Fail(out error);
                }
                else if (section.Name == "curvature" && field.Name == "direction")
                {
                    if (field.Value.ValueKind != JsonValueKind.String || !new[] { "straight", "up", "down", "left", "right" }.Contains(field.Value.GetString())) return Fail(out error);
                }
                else if (section.Name == "foreskin" && field.Name == "isCircumcised")
                {
                    if (field.Value.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) return Fail(out error);
                }
                else if (section.Name == "skin" && field.Name is "toneHex" or "glansColorHex")
                {
                    if (field.Value.ValueKind != JsonValueKind.String || !Regex.IsMatch(field.Value.GetString()!, @"^#[0-9a-fA-F]{6}$", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50))) return Fail(out error);
                }
                else if (section.Name == "skin" && field.Name == "veinTextureType")
                {
                    if (field.Value.ValueKind != JsonValueKind.String || field.Value.GetString() is not ("normal" or "height")) return Fail(out error);
                }
                else if (section.Name == "skin" && field.Name is "customVeinTexture" or "veinHeightTexture")
                {
                    if (field.Value.ValueKind == JsonValueKind.Null) continue;
                    if (field.Value.ValueKind != JsonValueKind.String || !ValidTexture(field.Value.GetString()!)) return Fail(out error);
                }
                else return Fail(out error);
            }
        }
        if (workspace && new[] { "id", "name", "dimensions", "curvature", "glans", "foreskin", "skin" }.Any(key => !keys.Contains(key))) return Fail(out error);
        return true;
    }
    private static bool Fail(out string? error) { error = "El payload anatómico tiene un esquema, tamaño o textura no admitidos."; return false; }

    public static bool ValidTexture(string data)
    {
        if (data.Length > SecurityOptions.MaxTextureCharacters) return false;
        var comma = data.IndexOf(',');
        if (comma < 0 || data[..comma] is not ("data:image/png;base64" or "data:image/jpeg;base64" or "data:image/webp;base64")) return false;
        var encoded = data.AsSpan(comma + 1);
        var buffer = ArrayPool<byte>.Shared.Rent(Math.Max(1, encoded.Length / 4 * 3 + 3));
        try
        {
            if (!Convert.TryFromBase64Chars(encoded, buffer, out var count)) return false;
            var bytes = buffer.AsSpan(0, count);
            int width = 0, height = 0;
            if (data.StartsWith("data:image/png;") && count >= 33 && bytes[..8].SequenceEqual(new byte[] { 137,80,78,71,13,10,26,10 }) && bytes.Slice(12,4).SequenceEqual("IHDR"u8))
            {
                var w = BinaryPrimitives.ReadUInt32BigEndian(bytes.Slice(16,4));
                var h = BinaryPrimitives.ReadUInt32BigEndian(bytes.Slice(20,4));
                if (w > 2048 || h > 2048) return false;
                width = (int)w; height = (int)h;
            }
            else if (data.StartsWith("data:image/jpeg;") && count > 4 && bytes[0] == 255 && bytes[1] == 216 && bytes[^2] == 255 && bytes[^1] == 217)
            {
                for (var offset = 2; offset + 4 < count;)
                {
                    if (bytes[offset++] != 255) return false;
                    while (offset < count && bytes[offset] == 255) offset++;
                    if (offset + 2 >= count) return false;
                    var marker = bytes[offset++];
                    if (marker is 0xD9 or 0xDA) break;
                    if (marker is 0x01 or >= 0xD0 and <= 0xD7) continue;
                    var length = BinaryPrimitives.ReadUInt16BigEndian(bytes.Slice(offset,2));
                    if (length < 2 || offset + length > count) return false;
                    if (marker is >= 0xC0 and <= 0xCF && marker is not (0xC4 or 0xC8 or 0xCC) && length >= 7)
                    {
                        height = BinaryPrimitives.ReadUInt16BigEndian(bytes.Slice(offset+3,2));
                        width = BinaryPrimitives.ReadUInt16BigEndian(bytes.Slice(offset+5,2));
                        break;
                    }
                    offset += length;
                }
            }
            else if (data.StartsWith("data:image/webp;") && count >= 30 && bytes[..4].SequenceEqual("RIFF"u8) && bytes.Slice(8,4).SequenceEqual("WEBP"u8))
            {
                if (BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(4,4)) != count - 8) return false;
                if (bytes.Slice(12,4).SequenceEqual("VP8X"u8))
                {
                    width = 1 + bytes[24] + (bytes[25] << 8) + (bytes[26] << 16);
                    height = 1 + bytes[27] + (bytes[28] << 8) + (bytes[29] << 16);
                }
                else if (bytes.Slice(12,4).SequenceEqual("VP8L"u8) && bytes[20] == 0x2F)
                {
                    width = 1 + (bytes[21] | ((bytes[22] & 63) << 8));
                    height = 1 + ((bytes[22] >> 6) | (bytes[23] << 2) | ((bytes[24] & 15) << 10));
                }
                else if (bytes.Slice(12,4).SequenceEqual("VP8 "u8) && bytes.Slice(23,3).SequenceEqual(new byte[] { 0x9D,1,0x2A }))
                {
                    width = BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(26,2)) & 0x3FFF;
                    height = BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(28,2)) & 0x3FFF;
                }
            }
            return width is > 0 and <= 2048 && height is > 0 and <= 2048 && (long)width * height <= 2_097_152;
        }
        finally { ArrayPool<byte>.Shared.Return(buffer, clearArray: true); }
    }
}

internal static class JsonNumberExtensions
{
    public static bool TryGetDoubleSafe(this JsonElement value, out double number)
    {
        number = 0;
        return value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out number);
    }
}
