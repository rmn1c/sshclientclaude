using System.Security.Cryptography;
using System.Text;

namespace SshClient.Infrastructure;

/// <summary>
/// Encrypts/decrypts sensitive strings using Windows DPAPI (Data Protection API).
/// Data is bound to the current Windows user account — unreadable by other users
/// or on other machines, and never stored as plain text.
/// </summary>
public static class DpapiProtector
{
    private static readonly byte[] s_entropy = "SshClient.v1"u8.ToArray();

    public static string Protect(string plainText)
    {
        var bytes = Encoding.UTF8.GetBytes(plainText);
        var encrypted = ProtectedData.Protect(bytes, s_entropy, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(encrypted);
    }

    public static string Unprotect(string cipherText)
    {
        var bytes = Convert.FromBase64String(cipherText);
        var decrypted = ProtectedData.Unprotect(bytes, s_entropy, DataProtectionScope.CurrentUser);
        return Encoding.UTF8.GetString(decrypted);
    }

    public static string? TryUnprotect(string? cipherText)
    {
        if (string.IsNullOrEmpty(cipherText)) return null;
        try { return Unprotect(cipherText); }
        catch { return null; }
    }
}
