# SSH Client

A modern, production-ready SSH client for Windows built with WPF (.NET 8). Dark-mode UI, custom ANSI terminal emulator, DPAPI-encrypted credential storage, and a searchable command-tips sidebar.

---

## Features

- **Password and private key authentication**
- **Saved sessions** — auto-filled on next launch, passwords encrypted with Windows DPAPI
- **Full terminal emulation** — VT100/VT220/xterm-256color, 256-colour + true-colour SGR, 2000-line scrollback
- **Command tips panel** — 65 tips across 5 categories, searchable, one-click send to terminal
- **Custom dark UI** — borderless window with native snap/resize, status bar, collapsible sidebar
- **Font size control** — `Ctrl+`/`Ctrl-`/`Ctrl+0`

---

## Building the .exe

### Prerequisites

| Requirement | Minimum version | Link |
|---|---|---|
| Windows | 10 (1903) or 11 | — |
| .NET SDK | **8.0** | [dotnet.microsoft.com/download](https://dotnet.microsoft.com/download) |

Confirm your SDK version:

```cmd
dotnet --version
```

The output must start with `8.` (e.g. `8.0.404`).

---

### Option A — Self-contained single .exe (recommended)

Produces one portable `.exe` (~65 MB). **No .NET runtime required** on the target machine.

**PowerShell:**
```powershell
git clone https://github.com/rmn1c/sshclientclaude.git
cd sshclientclaude

dotnet publish -c Release -r win-x64 --self-contained true `
    /p:PublishSingleFile=true `
    /p:EnableCompressionInSingleFile=true `
    /p:IncludeNativeLibrariesForSelfExtract=true `
    -o publish\release
```

**Command Prompt (cmd.exe):**
```cmd
git clone https://github.com/rmn1c/sshclientclaude.git
cd sshclientclaude

dotnet publish -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true /p:EnableCompressionInSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true -o publish\release
```

> **Tip:** If you'd rather not type the long command, just paste the single-line version from the CMD block above — it works in both PowerShell and cmd.exe.

Your executable is at:

```
publish\release\SshClient.exe
```

Copy it anywhere and run — no installer needed.

---

### Option B — Framework-dependent .exe (smaller download)

Produces a smaller `.exe` (~1 MB) but requires the [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) to be installed on the target machine.

```powershell
dotnet publish -c Release -r win-x64 --self-contained false -o publish\fdd
```

Executable: `publish\fdd\SshClient.exe`

---

### Option C — Run directly from source (development)

```powershell
git clone https://github.com/rmn1c/sshclientclaude.git
cd sshclientclaude
dotnet run
```

---

## Quick Start

1. Launch `SshClient.exe`
2. Type a **host**, **port** (default 22), and **username** in the top bar
3. Click **Connect** — a dialog opens for password or private key
4. Tick **Remember credentials** to save the session for next time
5. Start typing in the terminal

---

## Keyboard Shortcuts

| Shortcut | Action |
|---|---|
| `Ctrl+Shift+C` | Copy selected text |
| `Ctrl+Shift+V` | Paste into terminal |
| `Ctrl++` | Increase font size |
| `Ctrl+-` | Decrease font size |
| `Ctrl+0` | Reset font size to default |
| `Page Up / Page Down` | Scroll through terminal history |
| `↑ / ↓` | Shell command history (handled by remote shell) |
| `Ctrl+C` | Send interrupt signal (SIGINT) |
| `Ctrl+D` | Send EOF |
| `Ctrl+L` | Clear screen |

---

## Credential Security

Passwords are **never stored in plain text**. The flow is:

1. You authenticate successfully
2. The password is encrypted using **Windows DPAPI** (`ProtectedData.Protect`, `DataProtectionScope.CurrentUser`) with a per-application entropy value
3. Only the encrypted blob is written to `%APPDATA%\SshClient\sessions.json`

The ciphertext is bound to your Windows user account and machine — other users and other machines cannot decrypt it. Private key passphrases follow the same path; only the key file *path* is stored, never the key content.

---

## Project Structure

```
SshClient/
├── App.xaml / App.xaml.cs               Entry point, DI composition root
├── MainWindow.xaml / .xaml.cs           Window shell, custom chrome, keyboard wiring
│
├── Controls/
│   ├── TerminalBuffer.cs                Character grid + 2000-line scrollback ring buffer
│   ├── AnsiParser.cs                    VT100/VT220/xterm-256color state machine
│   ├── TerminalControl.cs               WPF FrameworkElement — GlyphRun renderer, mouse/keyboard
│   ├── CommandTipsPanel.xaml / .cs      Searchable accordion sidebar
│
├── ViewModels/
│   ├── MainViewModel.cs                 Connection state, terminal I/O, status bar
│   └── ConnectDialogViewModel.cs        Dialog state, saved-session management
│
├── Views/
│   └── ConnectDialog.xaml / .cs         Modal connection dialog
│
├── Services/
│   ├── SshService.cs                    SSH.NET shell stream + async read loop
│   └── SessionStore.cs                  JSON persistence with DPAPI-encrypted passwords
│
├── Infrastructure/
│   └── DpapiProtector.cs                ProtectedData Protect / Unprotect helpers
│
├── Resources/
│   └── Themes.xaml                      Dark-mode palette, control styles
│
└── Data/
    └── CommandTips.json                 Embedded tips (65 tips, 5 categories)
```

---

## Dependencies

| Package | Version |
|---|---|
| [SSH.NET](https://github.com/sshnet/SSH.NET) | 2023.0.0 |
| [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet) | 8.2.2 |
| [Microsoft.Extensions.DependencyInjection](https://learn.microsoft.com/dotnet/core/extensions/dependency-injection) | 8.0.0 |

All dependencies are restored automatically by `dotnet publish` / `dotnet run`.

---

## Known Limitations

- **Alt-screen apps** (vim, nano, htop) render into the main buffer — a full alternate-screen swap is not yet implemented.
- **Host key verification** uses SSH.NET's accept-all default. For stricter security, replace with a TOFU verifier backed by a local `known_hosts` file.
- **Single session** — one terminal per window. Multi-tab support would wrap `(TerminalControl, SshService)` pairs in a `TabControl`.
