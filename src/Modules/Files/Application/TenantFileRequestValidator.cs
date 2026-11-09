namespace Platform.Modules.Files.Application;

public static class TenantFileRequestValidator
{
    public const long MaxFileSizeBytes = 10 * 1024 * 1024;
    public const int MaxFileNameLength = 255;

    private static readonly IReadOnlyDictionary<string, (string ContentType, byte[] Signature)> AllowedTypes =
        new Dictionary<string, (string ContentType, byte[] Signature)>(StringComparer.OrdinalIgnoreCase)
        {
            [".pdf"] = ("application/pdf", "%PDF-"u8.ToArray()),
            [".png"] = ("image/png", [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]),
            [".jpg"] = ("image/jpeg", [0xFF, 0xD8, 0xFF]),
            [".jpeg"] = ("image/jpeg", [0xFF, 0xD8, 0xFF]),
        };

    public static string NormalizeFileName(string fileName)
    {
        return Path.GetFileName(fileName.Replace('\\', '/')).Trim();
    }

    public static Dictionary<string, string[]> Validate(
        string? fileName,
        string? contentType,
        long sizeBytes)
    {
        var errors = new Dictionary<string, string[]>();
        var normalizedFileName = fileName is null ? string.Empty : NormalizeFileName(fileName);

        if (string.IsNullOrWhiteSpace(normalizedFileName)
            || normalizedFileName.Length > MaxFileNameLength
            || normalizedFileName.Any(char.IsControl))
        {
            errors[nameof(fileName)] = [$"The file name must be 1 to {MaxFileNameLength} characters and contain no control characters."];
        }

        if (sizeBytes is < 1 or > MaxFileSizeBytes)
        {
            errors[nameof(sizeBytes)] = [$"The file size must be between 1 byte and {MaxFileSizeBytes} bytes."];
        }

        var extension = Path.GetExtension(normalizedFileName);
        if (!AllowedTypes.TryGetValue(extension, out var allowedType)
            || !string.Equals(contentType?.Trim(), allowedType.ContentType, StringComparison.OrdinalIgnoreCase))
        {
            errors[nameof(contentType)] = ["Only PDF, PNG, and JPEG files with a matching content type are allowed."];
        }

        return errors;
    }

    public static async Task<bool> HasValidSignatureAsync(
        string fileName,
        Stream content,
        CancellationToken cancellationToken)
    {
        var extension = Path.GetExtension(NormalizeFileName(fileName));
        if (!AllowedTypes.TryGetValue(extension, out var allowedType))
        {
            return false;
        }

        if (!content.CanSeek)
        {
            throw new InvalidOperationException("The uploaded file stream must support seeking for content validation.");
        }

        var originalPosition = content.Position;
        try
        {
            var signature = new byte[allowedType.Signature.Length];
            var read = 0;
            while (read < signature.Length)
            {
                var count = await content.ReadAsync(signature.AsMemory(read), cancellationToken);
                if (count == 0)
                {
                    return false;
                }

                read += count;
            }

            return signature.AsSpan().SequenceEqual(allowedType.Signature);
        }
        finally
        {
            content.Position = originalPosition;
        }
    }
}
