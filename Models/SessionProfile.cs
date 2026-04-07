namespace SshClient.Models;

public enum AuthType { Password, PrivateKey }

public class SessionProfile
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 22;
    public string Username { get; set; } = string.Empty;
    public AuthType AuthType { get; set; } = AuthType.Password;

    /// <summary>Stored encrypted via DPAPI; never plain-text on disk.</summary>
    public string? EncryptedPassword { get; set; }
    public string? PrivateKeyPath { get; set; }

    public DateTime LastConnected { get; set; }
    public bool RememberCredentials { get; set; }

    public override string ToString() =>
        string.IsNullOrWhiteSpace(Name) ? $"{Username}@{Host}:{Port}" : Name;
}
