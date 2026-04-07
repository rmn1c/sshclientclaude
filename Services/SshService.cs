using System.Text;
using Renci.SshNet;
using SshClient.Models;

namespace SshClient.Services;

public sealed class SshService : ISshService
{
    private Renci.SshNet.SshClient? _client;
    private ShellStream? _shell;
    private CancellationTokenSource? _readCts;

    public ConnectionState State { get; private set; } = ConnectionState.Disconnected;

    public event EventHandler<string>? DataReceived;
    public event EventHandler<ConnectionState>? StateChanged;
    public event EventHandler<string>? ErrorOccurred;

    public async Task<ConnectionResult> ConnectAsync(SessionProfile profile, string plainPassword, CancellationToken ct = default)
    {
        Disconnect();
        SetState(ConnectionState.Connecting);

        return await Task.Run(() =>
        {
            try
            {
                AuthenticationMethod auth = profile.AuthType == AuthType.PrivateKey && !string.IsNullOrEmpty(profile.PrivateKeyPath)
                    ? new PrivateKeyAuthenticationMethod(profile.Username,
                        new PrivateKeyFile(profile.PrivateKeyPath, string.IsNullOrEmpty(plainPassword) ? null : plainPassword))
                    : new PasswordAuthenticationMethod(profile.Username, plainPassword);

                var connectionInfo = new ConnectionInfo(profile.Host, profile.Port, profile.Username, auth)
                {
                    Timeout = TimeSpan.FromSeconds(15)
                };

                _client = new Renci.SshNet.SshClient(connectionInfo);
                _client.Connect();

                _shell = _client.CreateShellStream("xterm-256color", 220, 50, 0, 0, 65536);

                SetState(ConnectionState.Connected);
                StartReading();
                return new ConnectionResult(true);
            }
            catch (Exception ex)
            {
                SetState(ConnectionState.Error);
                ErrorOccurred?.Invoke(this, ex.Message);
                return new ConnectionResult(false, ex.Message);
            }
        }, ct);
    }

    public void SendInput(string text)
    {
        if (_shell is null || State != ConnectionState.Connected) return;
        try { _shell.Write(text); }
        catch (Exception ex) { ErrorOccurred?.Invoke(this, ex.Message); }
    }

    public void Resize(int cols, int rows)
    {
        if (_shell is null) return;
        try
        {
            // SendWindowChangeRequest was removed from the ShellStream public surface in SSH.NET 2023.
            // Invoke it via reflection if present; silently skip otherwise — the shell still works,
            // it just won't reflow to the new column/row count until reconnect.
            var method = _shell.GetType().GetMethod("SendWindowChangeRequest",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
            method?.Invoke(_shell, [(uint)cols, (uint)rows, 0u, 0u]);
        }
        catch { /* best-effort */ }
    }

    public void Disconnect()
    {
        _readCts?.Cancel();
        _readCts = null;

        try { _shell?.Dispose(); } catch { }
        try { _client?.Disconnect(); _client?.Dispose(); } catch { }

        _shell = null;
        _client = null;
        SetState(ConnectionState.Disconnected);
    }

    private void StartReading()
    {
        _readCts = new CancellationTokenSource();
        var token = _readCts.Token;
        var shell = _shell!;

        Task.Run(() =>
        {
            var buffer = new byte[65536];
            while (!token.IsCancellationRequested && (_client?.IsConnected ?? false))
            {
                try
                {
                    // ShellStream.Read blocks briefly then returns available bytes
                    int read = shell.Read(buffer, 0, buffer.Length);
                    if (read > 0)
                    {
                        var text = Encoding.UTF8.GetString(buffer, 0, read);
                        DataReceived?.Invoke(this, text);
                    }
                    else
                    {
                        Thread.Sleep(10);
                    }
                }
                catch (OperationCanceledException) { break; }
                catch
                {
                    if (!token.IsCancellationRequested)
                    {
                        SetState(ConnectionState.Error);
                    }
                    break;
                }
            }

            if (!token.IsCancellationRequested && State == ConnectionState.Connected)
                SetState(ConnectionState.Disconnected);
        }, token);
    }

    private void SetState(ConnectionState state)
    {
        State = state;
        StateChanged?.Invoke(this, state);
    }

    public void Dispose() => Disconnect();
}
