using System.Windows;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using SshClient.Services;
using SshClient.ViewModels;

namespace SshClient;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;

    public MainWindow(MainViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;

        vm.TerminalDataReceived += (_, data) => Terminal.FeedData(data);
        Terminal.UserInput += (_, text) => vm.SendRaw(text);
        Terminal.Resized += (_, size) => vm.NotifyResize(size.Item1, size.Item2);

        // Command tip "send to terminal" wired via code-behind (avoids cross-DataContext binding)
        TipsPanel.SendTipCommand = new RelayCommand<string>(
            tip => { if (!string.IsNullOrEmpty(tip)) vm.SendRaw(tip + "\r"); });

        // Global keyboard shortcuts
        KeyDown += MainWindow_KeyDown;

        // Snap corner radius when maximized
        StateChanged += (_, _) =>
        {
            RootBorder.BorderThickness = WindowState == WindowState.Maximized
                ? new Thickness(0)
                : new Thickness(1);
            MaxRestoreBtn.Content = WindowState == WindowState.Maximized ? "❐" : "□";
        };
    }

    // ---- Title bar interactions ----

    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            ToggleMaximize();
            return;
        }
        if (e.LeftButton == MouseButtonState.Pressed) DragMove();
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        => WindowState = WindowState.Minimized;

    private void MaximizeButton_Click(object sender, RoutedEventArgs e)
        => ToggleMaximize();

    private void CloseButton_Click(object sender, RoutedEventArgs e)
        => Close();

    private void ToggleMaximize()
        => WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;

    // ---- Connect / Disconnect ----

    private async void ConnectBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.IsConnected)
        {
            _vm.Disconnect();
            Terminal.Clear();
        }
        else
        {
            await _vm.QuickConnectAsyncCommand.ExecuteAsync(null);
        }
    }

    private async void Input_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
            await _vm.QuickConnectAsyncCommand.ExecuteAsync(null);
    }

    // ---- Terminal keyboard shortcut passthrough ----

    private void Terminal_KeyDown(object sender, KeyEventArgs e)
    {
        bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;

        if (ctrl && shift)
        {
            if (e.Key == Key.C)
            {
                var txt = Terminal.GetSelectedText();
                if (!string.IsNullOrEmpty(txt)) try { Clipboard.SetText(txt); } catch { }
                e.Handled = true;
            }
            else if (e.Key == Key.V)
            {
                if (Clipboard.ContainsText())
                    _vm.SendRaw(Clipboard.GetText());
                e.Handled = true;
            }
        }
    }

    private void MainWindow_KeyDown(object sender, KeyEventArgs e)
    {
        bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;

        if (ctrl && e.Key == Key.OemPlus)
        {
            _vm.TerminalFontSize = Math.Min(_vm.TerminalFontSize + 1, 28);
            e.Handled = true;
        }
        else if (ctrl && e.Key == Key.OemMinus)
        {
            _vm.TerminalFontSize = Math.Max(_vm.TerminalFontSize - 1, 8);
            e.Handled = true;
        }
        else if (ctrl && e.Key == Key.D0)
        {
            _vm.TerminalFontSize = 14;
            e.Handled = true;
        }

        // Ensure unhandled keys go to terminal
        if (!e.Handled && !ctrl && !shift && Terminal.IsVisible)
            Terminal.Focus();
    }

    // ---- Sidebar toggle ----

    private void ToggleSidebar_Click(object sender, RoutedEventArgs e)
        => _vm.ToggleSidebarCommand.Execute(null);

    protected override void OnClosed(EventArgs e)
    {
        _vm.Disconnect();
        base.OnClosed(e);
    }
}
