using System.ComponentModel.DataAnnotations;

namespace FitnessCenter.Web.Data;

public sealed class CatalogImage
{
    public const int MaxBytes = 5 * 1024 * 1024;

    public Guid Id { get; set; } = Guid.NewGuid();
    [MaxLength(20)] public string ContentType { get; set; } = "";
    public byte[] Content { get; set; } = [];

    public static string? DetectContentType(ReadOnlySpan<byte> content)
    {
        if (content.Length > MaxBytes) return null;
        if (content.StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })) return "image/png";
        if (content.StartsWith(new byte[] { 255, 216, 255 })) return "image/jpeg";
        if (content.Length >= 16 && content.StartsWith("RIFF"u8) && content[8..].StartsWith("WEBPVP8"u8)) return "image/webp";
        return null;
    }
}
