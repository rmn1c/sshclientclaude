using SshClient.Models;

namespace SshClient.Services;

public enum ConnectionState { Disconnected, Connecting, Connected, Reconnecting, Error }

public record ConnectionResult(bool Success, string? ErrorMessage = null);

public interface ISshService : IDisposable
{
    ConnectionState State { get; }
    event EventHandler<string> DataReceived;
    event EventHandler<ConnectionState> StateChanged;
    event EventHandler<string>? ErrorOccurred;

    Task<ConnectionResult> ConnectAsync(SessionProfile profile, string plainPassword, CancellationToken ct = default);
    void SendInput(string text);
    void Resize(int cols, int rows);
    void Disconnect();
}
