using System.IO;
using System.Text.Json;
using SshClient.Infrastructure;
using SshClient.Models;

namespace SshClient.Services;

/// <summary>
/// Persists session profiles to %APPDATA%\SshClient\sessions.json.
/// Passwords are DPAPI-encrypted before serialisation; they are never plain-text on disk.
/// </summary>
public class SessionStore : ISessionStore
{
    private static readonly string DataDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SshClient");

    private static readonly string FilePath = Path.Combine(DataDir, "sessions.json");

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private List<SessionProfile> _cache = [];

    public SessionStore() => Load();

    public IReadOnlyList<SessionProfile> GetAll() => _cache;

    public void Save(SessionProfile profile)
    {
        var existing = _cache.FindIndex(p => p.Id == profile.Id);
        if (existing >= 0) _cache[existing] = profile;
        else _cache.Add(profile);
        Persist();
    }

    public void Delete(Guid id)
    {
        _cache.RemoveAll(p => p.Id == id);
        Persist();
    }

    private void Load()
    {
        if (!File.Exists(FilePath)) return;
        try
        {
            var json = File.ReadAllText(FilePath);
            _cache = JsonSerializer.Deserialize<List<SessionProfile>>(json, JsonOpts) ?? [];
        }
        catch
        {
            _cache = [];
        }
    }

    private void Persist()
    {
        Directory.CreateDirectory(DataDir);
        var json = JsonSerializer.Serialize(_cache, JsonOpts);
        File.WriteAllText(FilePath, json);
    }

    // ----- Credential helpers (encrypt before save, decrypt on read) -----

    public static string? EncryptPassword(string? password)
        => string.IsNullOrEmpty(password) ? null : DpapiProtector.Protect(password);

    public static string? DecryptPassword(string? encrypted)
        => DpapiProtector.TryUnprotect(encrypted);
}
