using ErrandRuns.Application;

namespace ErrandRuns.Api;

public static class VerificationDocumentValidator
{
    public const long MaximumFileBytes = 10 * 1024 * 1024;

    public static async Task<DocumentUpload> Read(IFormFile file, CancellationToken ct)
    {
        if (file.Length is < 8 or > MaximumFileBytes)
            throw new ArgumentException("Verification document must be between 8 bytes and 10 MB.");
        await using var input = file.OpenReadStream();
        using var output = new MemoryStream((int)file.Length);
        await input.CopyToAsync(output, ct);
        var data = output.ToArray();
        var contentType = Detect(data) ?? throw new ArgumentException(
            "Verification document must be a valid JPEG, PNG, or PDF file.");
        return new(data, contentType, Path.GetFileName(file.FileName));
    }

    private static string? Detect(ReadOnlySpan<byte> data)
    {
        if (data.Length >= 3 && data[0] == 0xff && data[1] == 0xd8 && data[2] == 0xff)
            return "image/jpeg";
        if (data.Length >= 8 && data[..8].SequenceEqual(
                new byte[] { 0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a }))
            return "image/png";
        if (data.Length >= 5 && data[..5].SequenceEqual("%PDF-"u8))
            return "application/pdf";
        return null;
    }
}
