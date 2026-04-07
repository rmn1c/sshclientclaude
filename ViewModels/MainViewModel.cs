using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SshClient.Models;
using SshClient.Services;

namespace SshClient.ViewModels;

public partial class MainViewModel : BaseViewModel
{
    private readonly ISshService _ssh;
    private readonly ISessionStore _store;

    // ---- Quick-connect bar ----
    [ObservableProperty] private string _quickHost = string.Empty;
    [ObservableProperty] private int _quickPort = 22;
    [ObservableProperty] private string _quickUsername = string.Empty;

    // ---- State ----
    [ObservableProperty] private ConnectionState _connectionState = ConnectionState.Disconnected;
    [ObservableProperty] private string _statusText = "Not connected";
    [ObservableProperty] private string _sessionDuration = "--:--:--";
    [ObservableProperty] private string _currentDirectory = "~";
    [ObservableProperty] private string _encodingIndicator = "UTF-8";
    [ObservableProperty] private bool _isSidebarVisible = true;
    [ObservableProperty] private double _terminalFontSize = 14;
    [ObservableProperty] private string? _errorMessage;

    public bool IsConnected => ConnectionState == ConnectionState.Connected;
    public bool IsDisconnected => ConnectionState == ConnectionState.Disconnected || ConnectionState == ConnectionState.Error;

    private System.DateTime _connectedAt;
    private readonly System.Timers.Timer _uptimeTimer;

    public event EventHandler<string>? TerminalDataReceived;
    public event EventHandler<(int, int)>? RequestTerminalResize;

    public MainViewModel(ISshService ssh, ISessionStore store)
    {
        _ssh = ssh;
        _store = store;

        _ssh.StateChanged += OnSshStateChanged;
        _ssh.DataReceived += (_, data) => TerminalDataReceived?.Invoke(this, data);
        _ssh.ErrorOccurred += (_, msg) => Application.Current.Dispatcher.Invoke(() => ErrorMessage = msg);

        _uptimeTimer = new System.Timers.Timer(1000);
        _uptimeTimer.Elapsed += (_, _) =>
        {
            if (IsConnected)
            {
                var dur = System.DateTime.UtcNow - _connectedAt;
                Application.Current.Dispatcher.Invoke(() =>
                    SessionDuration = dur.ToString(@"hh\:mm\:ss"));
            }
        };
    }

    // ---- Commands ----

    [RelayCommand]
    private async Task QuickConnectAsync()
    {
        if (string.IsNullOrWhiteSpace(QuickHost))
        {
            ErrorMessage = "Host is required.";
            return;
        }

        var dlg = new Views.ConnectDialog(_store, QuickHost, QuickPort, QuickUsername);
        if (dlg.ShowDialog() != true) return;

        await ConnectWithProfileAsync(dlg.ResultProfile!, dlg.PlainPassword!);
    }

    public async Task ConnectWithProfileAsync(SessionProfile profile, string plainPassword)
    {
        ErrorMessage = null;
        var result = await _ssh.ConnectAsync(profile, plainPassword);
        if (!result.Success)
        {
            ErrorMessage = result.ErrorMessage;
            return;
        }

        // Persist session on successful auth
        if (profile.RememberCredentials)
        {
            profile.EncryptedPassword = SessionStore.EncryptPassword(plainPassword);
            profile.LastConnected = System.DateTime.UtcNow;
            _store.Save(profile);
        }

        QuickHost = profile.Host;
        QuickPort = profile.Port;
        QuickUsername = profile.Username;
    }

    [RelayCommand]
    private void Disconnect()
    {
        _ssh.Disconnect();
        SessionDuration = "--:--:--";
        CurrentDirectory = "~";
    }

    [RelayCommand]
    private void ToggleSidebar() => IsSidebarVisible = !IsSidebarVisible;

    [RelayCommand]
    private void SendCommand(string? cmd)
    {
        if (!string.IsNullOrEmpty(cmd) && IsConnected)
            _ssh.SendInput(cmd + "\r");
    }

    public void SendRaw(string text) => _ssh.SendInput(text);

    public void NotifyResize(int cols, int rows) => _ssh.Resize(cols, rows);

    // ---- State transitions ----

    private void OnSshStateChanged(object? sender, ConnectionState state)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            ConnectionState = state;
            OnPropertyChanged(nameof(IsConnected));
            OnPropertyChanged(nameof(IsDisconnected));

            StatusText = state switch
            {
                ConnectionState.Connecting => $"Connecting to {QuickHost}…",
                ConnectionState.Connected => $"Connected  {QuickUsername}@{QuickHost}:{QuickPort}",
                ConnectionState.Disconnected => "Disconnected",
                ConnectionState.Error => "Connection error",
                ConnectionState.Reconnecting => "Reconnecting…",
                _ => "Unknown"
            };

            if (state == ConnectionState.Connected)
            {
                _connectedAt = System.DateTime.UtcNow;
                _uptimeTimer.Start();
            }
            else
            {
                _uptimeTimer.Stop();
            }
        });
    }
}
