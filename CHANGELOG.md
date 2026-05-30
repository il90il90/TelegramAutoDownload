# Changelog

## [2.9.4] - 2026-05-24

### Added

- **Filter Include/Exclude mode** — each regex pattern in the Filter Editor can be set to Exclude (skip files / block text capture) or Include (whitelist files / capture matching text as .txt)
- Live test panel shows per-pattern and overall outcome for both file and message modes

### Changed

- Legacy `IgnoreFileByRegex` patterns are migrated automatically as **Exclude** rules

## [2.9.3] - 2026-05-24

### Added

- **Clear done** button in Downloads panel — manually remove completed rows (✔ Done only)

### Changed

- Downloads list sorted **newest first** (most recently added at the top)
- **Auto clean** checkbox moved to its own row with spacing from action buttons

## [2.9.2] - 2026-05-23

### Fixed

- **Duplicate download rows** for magnet links and URL text messages — the preview row (🧲/🔗) is renamed in-place when the plugin reports the real torrent/file name

## [2.9.1] - 2026-05-23

### Fixed

- **Start Icon for magnet links** — pasting a `magnet:` link (or http URL in text) now enqueues the download and sends the Start Icon reaction, same as file attachments

## [2.9.0] - 2026-05-23

### Added

- **Disk space bar** in the header — free space on the download drive, download folder size and file count; low-space warning colors
- **Auto clean** checkbox in Downloads panel — keep completed rows when unchecked
- **Open file** button (📂) on completed downloads — launch file or folder from the UI

### Fixed

- Magnet link metadata timeout (30 min DHT fetch) and improved peer discovery settings (from 2.8.9)

## [2.8.9] - 2026-05-23

### Fixed

- **Magnet links** — separate 30-minute timeout for metadata fetch (DHT) vs 10-minute download stall; enable DHT/PEX/local peer discovery and magnet metadata cache
- **Re-sending .torrent files** — no longer skipped as "already downloaded" when only the small `.torrent` metadata file exists from an older version (checks for actual content in `Torrent/` folder)

## [2.8.8] - 2026-05-23

### Fixed

- **Torrent downloads stuck at 0%** — enable UPnP/port forwarding (`AllowPortForwarding`) so MonoTorrent can connect to peers; use shared engine and proper multi-file torrent settings
- Progress now uses MonoTorrent `Progress` (includes hash-check phase), not only raw network bytes

### Added

- **Magnet links** in message text/captions — extracted even when embedded (e.g. `Download: magnet:?xt=...`); enable **TR** per chat

## [2.8.7] - 2026-05-22

### Added

- **Torrent plugin downloads real content** — `.torrent` file attachments from Telegram are parsed and downloaded via BitTorrent (MonoTorrent), not just saved as the small `.torrent` file
- **Magnet links** — progress, cancel, and completion callbacks wired to the downloads UI
- Shared `TorrentDownloadService` handles both magnet URIs and local `.torrent` files with byte-level progress reporting

## [2.8.6] - 2026-05-22

### Fixed

- **Video/file downloads failing after ~3–6 seconds** with "Download cancelled (no progress)" — reconnect no longer interrupts active transfers; each retry gets a fresh progress token; downloads wait for Telegram connection before starting
- **Manual Cancel** logs as `[INF] Download cancelled by user` instead of `[WRN] Download failed`, and no longer triggers the error alert button
- User cancel is correctly classified as `"Cancelled by user"` (not confused with timeout/no-progress)

## [2.8.5] - 2026-05-22

### Fixed

- **Plain text messages without URLs** no longer log as `[WRN] Download failed` or trigger the error alert button — expected "nothing to download" cases are silent

## [2.8.4] - 2026-05-22

### Fixed

- **session.dat locked after update** — WTelegram client is disposed before exit; startup retries up to 40 seconds when the session file is temporarily locked
- **Single-instance mutex** released only after session handles are closed
- **Downloads aborted after ~6 seconds** — connection monitor no longer calls `ConnectAsync` while file transfers are in progress
- **Auto-update batch script** force-kills leftover processes and waits before running the installer

## [2.8.3] - 2026-05-22

### Fixed

- **In-app update (Setup.exe)** no longer fails with "Setup was unable to automatically close all applications" — the app exits fully before the installer runs, and the installer force-closes any leftover process

## [2.8.2] - 2026-05-22

### Fixed

- **Log viewer delete** no longer fails with "file is in use" when deleting today's active log — Serilog is closed and reopened automatically
- Serilog file sink uses `shared: true` so log files can be read while the app is running

## [2.8.1] - 2026-05-22

### Fixed

- **WTelegram KeepAlive network drops** no longer log as `[ERR]` — transient TCP timeouts are downgraded to `[WRN]` and the app auto-reconnects
- **Single-instance guard** prevents two app copies from locking `session.dat` on startup
- **Connection status dot** in the footer now reflects live Telegram connection state (green/red)
- **Background login failures** are logged clearly instead of being swallowed silently
- **`Client_OnUpdates` handler** wrapped in try/catch so update-processing errors cannot crash the session
- **`CHAT_ADMIN_REQUIRED`** on member export is logged at Debug level (expected when user is not admin)

## [2.7.14] - 2026-05-15

### Note

- **Stable rollback release** — same codebase as v2.7.4 (recommended if v2.7.5–v2.7.13 caused login, chat list, or download issues). Version **2.7.14** so the in-app updater can install it over newer builds.

## [2.0.0] - 2026-05-05

### Added

- **SocialMediaPlugin**: download any yt-dlp-supported site (Instagram, TikTok, Twitter, Reddit, …) — Priority 2
- **TorrentPlugin**: download magnet links via MonoTorrent — Priority 3
- `DownloadProgressService`: thread-safe singleton with `ObservableCollection<DownloadItem>` for real-time progress tracking
- `DownloadItem` model with `INotifyPropertyChanged` for data-binding
- **Priority system** on `IBasePlugin`/`BasePlugin` — plugins run in ascending priority order; first `IsSuccess = true` wins
- **Per-chat provider toggles** (`EnabledPlugins` dict on `ChatDto` and `Config`) — UI checkboxes in new Providers column
- `DownloadThreads` field on `ConfigParams` (default 3) with live slider in the status bar
- `SemaphoreSlim` in `TelegramApp` to honor `DownloadThreads` limit
- `QRCoder.Xaml` QR code login tab in `LoginWindow`
- `MahApps.Metro 2.4.11` modern UI framework with Telegram blue (#2AABEE) accent
- Active Downloads panel in `MainWindow` (DataGrid with progress bars)
- Type badges (Channel=blue, Group=green, User=purple) in chat list
- Alternating row colors + hover highlight
- HTML-formatted Telegram bot notifications (`parse_mode=HTML`)
- Unit test project `TelegramAutoDownload.Tests` (xUnit + FluentAssertions)
- yt-dlp.exe auto-downloaded to `tools\` folder

### Fixed

- **Group.cs**: used the already-matched `group` object for `chatParams` lookup instead of re-querying; added `null` guard on `chatParams`
- **BaseMessage.cs**: corrected inverted size-threshold condition (`documentSizeInMb < DownloadFromSize` → skip)
- **User.cs**: replaced unsafe `((Updates)updates)` cast with safe `updates as Updates` pattern; added `null` guard on `chatParams`
- **TelegramApp.cs**:
  - `ReactToMessage` wrapped in its own try/catch so reaction failure does not suppress `OnSaved`
  - Fixed typo `isCahnnel` → `isChannel`
  - Added `updates.Chats.Count > 0` guards before `.First()` calls
  - `OnSaved` and `OnWarnningMessage` delegates are now properly awaited
  - `configParams.Chats` null-checked in `UpdateConfig`
- **MainWindow.xaml.cs**:
  - `SelectChatId_Checked`: merges by ID instead of discarding all unselected chats
  - `HlOpenFolder_Click`: safe `as Run` cast + null check
  - `Download_Checked` / `DownloadSize_TextChanged`: use injected `ConfigFile` instead of `new ConfigFile()`
  - `DownloadSize_TextChanged`: fixed `textBox = "0"` → `textbox.Text = "0"`
  - `LoadDataAsync`: null-checked `fromConfigFile.Download` before copying flags
- **ConfigFile.cs**: `Read()` now returns `new ConfigParams()` on null/corrupt JSON
- **LoginWindow.xaml.cs**: `BtnLogin_Click` wrapped in try/catch
- **FactoryMessagesService.cs**: null-safe `mime_type?.Contains(…) == true`
- **MessageTextFactoryService.cs**: null-safe `(message.message ?? string.Empty).Split('\n')`
- **Photos.cs**: filename assigned before duplicate check; `Split('/').Last()` replaces fragile `IndexOf` substring
- **Videos.cs / Files.cs / Music.cs**: `File.OpenWrite` → `File.Create` (truncates on re-download); removed redundant `client` field
- **YouTubePlugin.cs**: returns `IsSuccess = false` with message "No mp4 stream available" when `streamInfo == null`
- **App.xaml.cs**: `DotEnv.Load()` moved to `OnStartup`; removed unused `System.Configuration` / `System.Data`
- **ConfigParams.cs**: hardcoded `AppId`/`ApiHash` replaced with `Environment.GetEnvironmentVariable("APP_ID/API_HASH")`
- **TelegramService.cs**: removed redundant `await Task.CompletedTask`; added `parse_mode=HTML`
- **FactoryUserService.cs**: removed unused `System.Threading.Channels` import
- Minor XAML cleanup: fixed `microsofft.com` typo, removed dead `SearchText` binding and broken Space key binding

### Changed

- **YoutubeExplode** upgraded from `6.3.16` → `6.5.7`
- **WTelegramClient** stays at `4.1.1` (QR login implemented without version bump)
- `IBasePlugin` now requires `int Priority { get; }`
- Plugin execution loop: sorted by Priority, respects `EnabledPlugins`, breaks on first success
- `Notification.cs`: rich HTML messages instead of raw JSON dumps
- `MainWindow` and `LoginWindow` converted to `MahApps.Metro.Controls.MetroWindow`
- `LoginWindow` redesigned with TabControl (Phone + QR tabs), step-by-step flow, inline error display

### Security

- Credentials (`APP_ID`, `API_HASH`) no longer hardcoded in source; must be set via `.env`
- `config.txt` and `.env` added to `.gitignore`
- `session.dat` already covered by `*.dat` rule
