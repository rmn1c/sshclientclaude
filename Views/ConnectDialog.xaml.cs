using System.Windows;
using System.Windows.Controls;
using SshClient.Models;
using SshClient.Services;

namespace SshClient.Views;

public partial class ConnectDialog : Window
{
    public SessionProfile? ResultProfile { get; private set; }
    public string? PlainPassword { get; private set; }

    private readonly ISessionStore _store;
    private readonly List<SessionProfile> _sessions;

    public ConnectDialog(ISessionStore store,
        string? preHost = null, int prePort = 22, string? preUser = null)
    {
        _store = store;
        InitializeComponent();

        _sessions = [.. store.GetAll()];
        SavedSessionsBox.ItemsSource = _sessions;
        SavedSessionsBox.DisplayMemberPath = "Name";

        if (preHost is not null) HostBox.Text = preHost;
        PortBox.Text = prePort.ToString();
        if (preUser is not null) UsernameBox.Text = preUser;

        HostBox.Focus();
    }

    private void TitleBar_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed) DragMove();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void Auth_Changed(object sender, RoutedEventArgs e)
    {
        bool isKey = KeyRadio.IsChecked == true;
        PasswordPanel.Visibility = isKey ? Visibility.Collapsed : Visibility.Visible;
        KeyPanel.Visibility = isKey ? Visibility.Visible : Visibility.Collapsed;
    }

    private void BrowseKey_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Select Private Key File",
            Filter = "Key files (*.pem;*.ppk;*.key;id_rsa;id_ed25519)|*.pem;*.ppk;*.key;id_rsa;id_ed25519|All files (*.*)|*.*"
        };
        if (dlg.ShowDialog() == true)
            KeyPathBox.Text = dlg.FileName;
    }

    private void SavedSessions_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SavedSessionsBox.SelectedItem is not SessionProfile p) return;

        HostBox.Text = p.Host;
        PortBox.Text = p.Port.ToString();
        UsernameBox.Text = p.Username;
        ProfileNameBox.Text = p.Name;
        RememberCheck.IsChecked = p.RememberCredentials;

        if (p.AuthType == AuthType.PrivateKey)
        {
            KeyRadio.IsChecked = true;
            KeyPathBox.Text = p.PrivateKeyPath ?? string.Empty;
        }
        else
        {
            PasswordRadio.IsChecked = true;
            if (p.RememberCredentials && !string.IsNullOrEmpty(p.EncryptedPassword))
                PasswordBox.Password = SessionStore.DecryptPassword(p.EncryptedPassword) ?? string.Empty;
        }
    }

    private void DeleteSession_Click(object sender, RoutedEventArgs e)
    {
        if (SavedSessionsBox.SelectedItem is SessionProfile p)
        {
            _store.Delete(p.Id);
            _sessions.Remove(p);
            SavedSessionsBox.ItemsSource = null;
            SavedSessionsBox.ItemsSource = _sessions;
        }
    }

    private void Connect_Click(object sender, RoutedEventArgs e)
    {
        ErrorLabel.Text = string.Empty;

        if (string.IsNullOrWhiteSpace(HostBox.Text))
        { ErrorLabel.Text = "Host is required."; return; }

        if (!int.TryParse(PortBox.Text, out int port) || port is < 1 or > 65535)
        { ErrorLabel.Text = "Invalid port number."; return; }

        if (string.IsNullOrWhiteSpace(UsernameBox.Text))
        { ErrorLabel.Text = "Username is required."; return; }

        var authType = KeyRadio.IsChecked == true ? AuthType.PrivateKey : AuthType.Password;

        if (authType == AuthType.PrivateKey && string.IsNullOrWhiteSpace(KeyPathBox.Text))
        { ErrorLabel.Text = "Select a private key file."; return; }

        var existing = SavedSessionsBox.SelectedItem as SessionProfile;
        ResultProfile = existing ?? new SessionProfile();
        ResultProfile.Host = HostBox.Text.Trim();
        ResultProfile.Port = port;
        ResultProfile.Username = UsernameBox.Text.Trim();
        ResultProfile.AuthType = authType;
        ResultProfile.PrivateKeyPath = authType == AuthType.PrivateKey ? KeyPathBox.Text.Trim() : null;
        ResultProfile.RememberCredentials = RememberCheck.IsChecked == true;
        ResultProfile.Name = string.IsNullOrWhiteSpace(ProfileNameBox.Text)
            ? $"{ResultProfile.Username}@{ResultProfile.Host}"
            : ProfileNameBox.Text.Trim();

        PlainPassword = authType == AuthType.PrivateKey
            ? PassphraseBox.Password
            : PasswordBox.Password;

        DialogResult = true;
    }
}
