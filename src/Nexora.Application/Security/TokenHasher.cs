using System.Security.Cryptography;
using System.Text;

namespace Nexora.Application.Security;

public static class TokenHasher
{
    /// <summary>Creates a 512-bit random, URL safe opaque token.</summary>
    public static string NewOpaqueToken() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(64)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>SHA-256 is enough here: the input is 512 bits of randomness, so there is nothing to brute force.</summary>
    public static string Sha256(string value) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    public static string NewWebhookSecret() => "whsec_" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(24));
}

public static class NexoraClaims
{
    public const string Subject = "sub";
    public const string Email = "email";
    public const string Name = "name";
    public const string Role = "role";
    public const string TenantId = "tenant_id";
    public const string BranchId = "branch_id";
    public const string SecurityStamp = "sstamp";
    public const string Jti = "jti";
}
