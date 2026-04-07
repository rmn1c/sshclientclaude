# SSH Client — Build & Run Guide

## Prerequisites

| Requirement | Version | Download |
|---|---|---|
| .NET SDK | 8.0+ | https://dotnet.microsoft.com/download |
| Windows | 10 1903+ / 11 | Required for WPF |
| Git | any | (optional) |

Verify your SDK:
```
dotnet --version   # must be 8.x or newer
```

---

## Quick Build (Debug)

```cmd
cd sshclientclaude
dotnet restore
dotnet build
dotnet run
```

---

## Release Build — Framework-Dependent .exe

Smallest output; requires .NET 8 runtime on the target machine.

```cmd
dotnet publish -c Release -r win-x64 --self-contained false -o publish\fdd
```

The `.exe` is at `publish\fdd\SshClient.exe`.

---

## Release Build — Self-Contained Single .exe

No runtime required on the target machine (~65 MB).

```cmd
dotnet publish -c Release -r win-x64 --self-contained true `
    /p:PublishSingleFile=true `
    /p:IncludeNativeLibrariesForSelfExtract=true `
    /p:EnableCompressionInSingleFile=true `
    -o publish\singlefile
```

Deliverable: `publish\singlefile\SshClient.exe`

---

## Dependencies

| Package | Version | Purpose |
|---|---|---|
| [SSH.NET](https://github.com/sshnet/SSH.NET) | 2023.0.0 | SSH protocol, shell streams |
| [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet) | 8.2.2 | ObservableObject, RelayCommand, source generators |
| [Microsoft.Extensions.DependencyInjection](https://learn.microsoft.com/dotnet/core/extensions/dependency-injection) | 8.0.0 | DI container |

---

## Project Structure

```
SshClient/
├── App.xaml / App.xaml.cs          — Application entry point, DI composition root
├── MainWindow.xaml / .xaml.cs      — Main window (custom chrome, layout shell)
│
├── Models/
│   ├── SessionProfile.cs           — Connection profile data model
│   └── CommandCategory.cs          — Command-tip data models
│
├── ViewModels/
│   ├── BaseViewModel.cs            — ObservableObject base
│   ├── MainViewModel.cs            — Main state (connection, terminal I/O, status bar)
│   └── ConnectDialogViewModel.cs   — Connect dialog state + saved-sessions logic
│
├── Views/
│   └── ConnectDialog.xaml / .cs    — Modal connection dialog
│
├── Controls/
│   ├── TerminalBuffer.cs           — VT character grid + scrollback ring buffer
│   ├── AnsiParser.cs               — VT100/VT220/xterm-256color ANSI state machine
│   ├── TerminalControl.cs          — WPF FrameworkElement, GlyphRun renderer, input
│   ├── CommandTipsPanel.xaml / .cs — Searchable command-tip accordion sidebar
│
├── Services/
│   ├── ISshService.cs              — SSH service interface
│   ├── SshService.cs               — SSH.NET wrapper (shell stream, async read loop)
│   ├── ISessionStore.cs            — Session persistence interface
│   └── SessionStore.cs             — JSON file store with DPAPI-encrypted passwords
│
├── Infrastructure/
│   └── DpapiProtector.cs           — Windows DPAPI encrypt/decrypt helpers
│
├── Converters/
│   └── Converters.cs               — WPF value converters (bool→visibility, state→colour)
│
├── Resources/
│   └── Themes.xaml                 — Dark-mode colour palette, control styles
│
└── Data/
    └── CommandTips.json            — Embedded command-tip content (5 categories, 65 tips)
```

---

## Security Design

### Credential Storage
- Passwords are **never written to disk in plain text**.
- After successful login, the password is encrypted with **Windows DPAPI** (`ProtectedData.Protect`, `DataProtectionScope.CurrentUser`) with a per-app entropy value before being stored in `%APPDATA%\SshClient\sessions.json`.
- DPAPI binds ciphertext to the **current Windows user account and machine**. Other users and other machines cannot decrypt it.
- Private key passphrases follow the same path; the private key *file path* is stored (not the key content).

### Memory
- Passwords exist in memory only long enough to authenticate; the `PlainPassword` string is not retained in any long-lived object after the SSH handshake.
- `ShellStream` traffic flows directly between SSH.NET and the terminal buffer — no secondary logging or persistence.

### Network
- All traffic is end-to-end encrypted by SSH (AES/ChaCha20, negotiated by the server).
- Host key verification uses SSH.NET's default policy (accept on first connection). For production hardening, replace `SshService.cs` with a `KnownHostsVerifier` that reads `~/.ssh/known_hosts`.

---

## Keyboard Shortcuts

| Shortcut | Action |
|---|---|
| `Ctrl+Shift+C` | Copy terminal selection |
| `Ctrl+Shift+V` | Paste into terminal |
| `Ctrl++` | Increase font size |
| `Ctrl+-` | Decrease font size |
| `Ctrl+0` | Reset font size |
| `↑` `↓` | Command history (shell-side) |
| `Page Up/Down` | Scroll terminal history |

---

## Known Limitations & Improvement Notes

1. **Host key verification** — currently accepts all host keys (SSH.NET default). A production deployment should implement TOFU (Trust On First Use) against a local `known_hosts` file.
2. **Alt-screen** (vim, nano, htop) — the alternate screen escape (`ESC[?1049h/l`) is swallowed; these apps will render in the main buffer. A full implementation requires a second `TerminalBuffer` and swap logic.
3. **Ligature fonts** — GlyphRun renders one glyph per code point. Ligature sequences (e.g. `!=` → `≠` in Fira Code) are not collapsed. Switch to `FormattedText` if ligatures are needed.
4. **Tabs** — single-session only. Add a `TabControl` wrapping `(TerminalControl, SshService)` pairs for multi-tab support.
5. **Auto-reconnect** — implement by catching `ConnectionState.Error` in `MainViewModel` and retrying `ConnectWithProfileAsync` with exponential back-off.
