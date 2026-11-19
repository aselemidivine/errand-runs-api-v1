using ErrandRuns.Application;
using Microsoft.Extensions.Configuration;

namespace ErrandRuns.Infrastructure.Identity;

public sealed class LocalPrivateDocumentStorage(IConfiguration configuration) : IPrivateDocumentStorage
{
    private readonly string root = Path.GetFullPath(configuration["PrivateDocuments:RootPath"]
        ?? Path.Combine(AppContext.BaseDirectory, "private-documents"));

    public async Task<string> Save(Guid ownerId, string purpose, DocumentUpload document, CancellationToken ct)
    {
        var extension = document.ContentType.ToLowerInvariant() switch
        {
            "image/jpeg" => ".jpg",
            "image/png" => ".png",
            "application/pdf" => ".pdf",
            _ => throw new ArgumentException("Unsupported verification document type.")
        };
        var safePurpose = new string(purpose.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
        var relative = Path.Combine(ownerId.ToString("N"), safePurpose,
            $"{Guid.NewGuid():N}{extension}");
        var path = Path.GetFullPath(Path.Combine(root, relative));
        if (!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Invalid private document path.");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllBytesAsync(path, document.Data, ct);
        return relative.Replace(Path.DirectorySeparatorChar, '/');
    }

    public Task Delete(string objectKey, CancellationToken ct)
    {
        var relative = objectKey.Replace('/', Path.DirectorySeparatorChar);
        var path = Path.GetFullPath(Path.Combine(root, relative));
        if (!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Invalid private document path.");
        if (File.Exists(path)) File.Delete(path);
        return Task.CompletedTask;
    }
}
