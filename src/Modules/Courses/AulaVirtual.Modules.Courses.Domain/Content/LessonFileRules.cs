namespace AulaVirtual.Modules.Courses.Domain.Content;

public enum LessonFileKind
{
    Material,
    Video
}

// What a lesson file may be (SCRUM-58: «límite de tamaño definido»). The same rules apply when the upload is
// requested and again when the uploaded file is attached, because the client uploads straight to storage.
public static class LessonFileRules
{
    public const long MaxMaterialBytes = 50L * 1024 * 1024;
    public const long MaxVideoBytes = 2L * 1024 * 1024 * 1024;

    public static readonly IReadOnlySet<string> MaterialTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "application/pdf",
        "image/png",
        "image/jpeg",
        "image/webp",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        "application/vnd.openxmlformats-officedocument.presentationml.presentation",
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
    };

    public static readonly IReadOnlySet<string> VideoTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "video/mp4",
        "video/webm"
    };

    public static bool TryParseKind(string? value, out LessonFileKind kind)
    {
        switch (value?.Trim().ToLowerInvariant())
        {
            case "material": kind = LessonFileKind.Material; return true;
            case "video": kind = LessonFileKind.Video; return true;
            default: kind = default; return false;
        }
    }

    /// <summary>Null when the file is acceptable; otherwise a message for the administrator.</summary>
    public static string? Problem(LessonFileKind kind, string? contentType, long size)
    {
        var (types, max, label) = kind == LessonFileKind.Video
            ? (VideoTypes, MaxVideoBytes, "MP4 o WebM")
            : (MaterialTypes, MaxMaterialBytes, "PDF, imagen (PNG, JPG, WebP) o documento de Office");
        if (string.IsNullOrWhiteSpace(contentType) || !types.Contains(contentType.Trim()))
            return $"Tipo de archivo no permitido: usa {label}.";
        if (size <= 0) return "El archivo está vacío.";
        if (size > max) return $"El archivo supera el máximo de {max / (1024 * 1024)} MB.";
        return null;
    }
}
