using System.Security.Cryptography;
using System.Text;
using ErrandRuns.Application;
using Microsoft.Extensions.Configuration;

namespace ErrandRuns.Infrastructure;

public sealed class CompletionCodeProtector(IConfiguration configuration) : ICompletionCodeProtector
{
    public CompletionCodeChallenge Generate(Guid errandId)
    {
        var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
        return new(code, Hash(errandId, code));
    }

    public bool Verify(Guid errandId, string code, string hash)
    {
        if (code?.Length != 6 || code.Any(character => !char.IsDigit(character))) return false;
        try
        {
            return CryptographicOperations.FixedTimeEquals(
                Convert.FromBase64String(Hash(errandId, code)), Convert.FromBase64String(hash));
        }
        catch (FormatException) { return false; }
    }

    private string Hash(Guid errandId, string code)
    {
        var key = configuration["CompletionCode:SigningKey"]
            ?? configuration["Jwt:SigningKey"]
            ?? throw new InvalidOperationException("CompletionCode:SigningKey or Jwt:SigningKey is required.");
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(key));
        return Convert.ToBase64String(hmac.ComputeHash(
            Encoding.UTF8.GetBytes($"{errandId:N}:{code}")));
    }
}
