using System.Security.Cryptography;
using System.Text;

namespace M1vClock.Core.Security;

public static class SecretProtector
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("m1vClock.secrets.v1");

    public static string Protect(string secret)
    {
        ArgumentException.ThrowIfNullOrEmpty(secret);
        var bytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(secret), Entropy, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(bytes);
    }

    public static string Unprotect(string protectedSecret)
    {
        ArgumentException.ThrowIfNullOrEmpty(protectedSecret);
        var bytes = ProtectedData.Unprotect(Convert.FromBase64String(protectedSecret), Entropy, DataProtectionScope.CurrentUser);
        return Encoding.UTF8.GetString(bytes);
    }
}
