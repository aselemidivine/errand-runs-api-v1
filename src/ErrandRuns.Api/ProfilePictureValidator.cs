using ErrandRuns.Application;

namespace ErrandRuns.Api;

public static class ProfilePictureValidator
{
    public const long MaximumFileBytes = 5 * 1024 * 1024;

    public static async Task<ProfilePictureUpload> Read(IFormFile file, CancellationToken ct)
    {
        if (file.Length is < 8 or > MaximumFileBytes)
            throw new ArgumentException("Profile picture must be between 8 bytes and 5 MB.");

        await using var input = file.OpenReadStream();
        using var output = new MemoryStream((int)file.Length);
        await input.CopyToAsync(output, ct);
        var data = output.ToArray();
        var contentType = DetectContentType(data)
            ?? throw new ArgumentException("Profile picture must be a valid JPEG, PNG, or WebP image.");
        return new ProfilePictureUpload(data, contentType);
    }

    public static string? DetectContentType(ReadOnlySpan<byte> data)
    {
        if (data.Length >= 3 && data[0] == 0xff && data[1] == 0xd8 && data[2] == 0xff)
            return "image/jpeg";
        if (data.Length >= 8
            && data[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a }))
            return "image/png";
        if (data.Length >= 12
            && data[..4].SequenceEqual("RIFF"u8)
            && data.Slice(8, 4).SequenceEqual("WEBP"u8))
            return "image/webp";
        return null;
    }
}
