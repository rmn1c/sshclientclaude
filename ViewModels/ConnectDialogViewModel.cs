using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SshClient.Models;
using SshClient.Services;

namespace SshClient.ViewModels;

public partial class ConnectDialogViewModel : BaseViewModel
{
    private readonly ISessionStore _store;

    [ObservableProperty] private string _host = string.Empty;
    [ObservableProperty] private int _port = 22;
    [ObservableProperty] private string _username = string.Empty;
    [ObservableProperty] private string _password = string.Empty;
    [ObservableProperty] private string _privateKeyPath = string.Empty;
    [ObservableProperty] private AuthType _authType = AuthType.Password;
    [ObservableProperty] private bool _rememberMe = true;
    [ObservableProperty] private string? _profileName;
    [ObservableProperty] private SessionProfile? _selectedSaved;
    [ObservableProperty] private List<SessionProfile> _savedSessions = [];

    public bool IsPassword => AuthType == AuthType.Password;
    public bool IsKey => AuthType == AuthType.PrivateKey;

    partial void OnAuthTypeChanged(AuthType value)
    {
        OnPropertyChanged(nameof(IsPassword));
        OnPropertyChanged(nameof(IsKey));
    }

    public ConnectDialogViewModel(ISessionStore store)
    {
        _store = store;
        SavedSessions = [.. store.GetAll()];
    }

    [RelayCommand]
    private void LoadSession(SessionProfile? profile)
    {
        if (profile is null) return;
        Host = profile.Host;
        Port = profile.Port;
        Username = profile.Username;
        AuthType = profile.AuthType;
        PrivateKeyPath = profile.PrivateKeyPath ?? string.Empty;
        ProfileName = profile.Name;
        RememberMe = profile.RememberCredentials;

        if (profile.RememberCredentials && !string.IsNullOrEmpty(profile.EncryptedPassword))
            Password = SessionStore.DecryptPassword(profile.EncryptedPassword) ?? string.Empty;
    }

    [RelayCommand]
    private void BrowseKey()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Select Private Key File",
            Filter = "Key files (*.pem;*.ppk;*.key;id_rsa;id_ed25519)|*.pem;*.ppk;*.key;id_rsa;id_ed25519|All files (*.*)|*.*"
        };
        if (dialog.ShowDialog() == true)
            PrivateKeyPath = dialog.FileName;
    }

    [RelayCommand]
    private void DeleteSession(SessionProfile? profile)
    {
        if (profile is null) return;
        _store.Delete(profile.Id);
        SavedSessions = [.. _store.GetAll()];
    }

    public SessionProfile BuildProfile()
    {
        var profile = SelectedSaved ?? new SessionProfile();
        profile.Host = Host.Trim();
        profile.Port = Port;
        profile.Username = Username.Trim();
        profile.AuthType = AuthType;
        profile.PrivateKeyPath = PrivateKeyPath;
        profile.RememberCredentials = RememberMe;
        profile.Name = ProfileName ?? $"{Username}@{Host}";
        return profile;
    }
}
