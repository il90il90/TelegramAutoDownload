# Telegram Auto Download

> **v2.3.0** — A WPF desktop application for Windows that monitors Telegram channels, groups, and DMs and automatically downloads media, YouTube/social-media links, and torrents.

---

## Features

### Download
- **Automatic media download** — videos, photos, music, and files from any monitored Telegram chat
- **Resume interrupted downloads** — `.part` files track progress; if the app closes mid-download the next attempt resumes from where it left off
- **Deduplication** — each file is tracked by its Telegram document ID; already-downloaded files are skipped automatically
- **Configurable minimum file size** per chat (skip files below N MB)
- **Parallel downloads** — configurable thread count (1–10, default 3)
- **Retry on failure** — a Retry button (↺) appears next to failed or timed-out downloads

### Per-chat settings
- **Per-chat provider toggles** — enable/disable YouTube, SocialMedia, Torrent, or Direct per chat
- **Per-chat yt-dlp quality** — choose Best, 4K, 1080p, 720p, 480p, or Audio-only per chat
- **Per-chat regex filters** — skip files whose name matches any semicolon-separated pattern (e.g. `\.jpg$; thumb_`)
- **Per-chat folder template** — customise the download path with tokens: `{Type}`, `{ChatName}`, `{Year}`, `{Month}`, `{Day}`
- **Emoji reactions** — send a reaction when a download starts and/or completes (configurable per chat)
- **Mute toggle** — mute/unmute Telegram notifications for a chat directly from the app

### Sync & history
- **History sync** — scan the full history of a chat and download everything that matches the current settings and hasn't been downloaded yet
- **Missed-update catch-up** — when `UpdateChannelTooLong` is received, the app fetches and processes any missed messages automatically
- **Sticker / voice message filtering** — stickers and voice messages are silently skipped

### Scheduling
- **Quiet Hours** — configure a daily time window (supports overnight, e.g. 23:00–07:00) during which no new downloads are started; use Sync afterwards to catch up

### Statistics & monitoring
- **Session stats** in the header strip — files downloaded and bytes transferred since last launch
- **All-time persistent stats** — total file count and bytes saved across all sessions (stored in `stats.json`)
- **Real-time progress** — speed, ETA, and percentage for every active download
- **Total speed badge** — combined download speed of all active downloads

### Notifications
- **Telegram bot** — optional notification on start/progress/complete/error (HTML-formatted)
- **Windows tray icon** — app continues running in the system tray when the window is closed

### Update & version
- **Auto-update check on startup** — compares current version to the latest GitHub release
- **Manual check** — click the version button (`vX.X.X`) to check for updates at any time; if already up to date the button briefly shows "✓ Up to date"
- **Smart update type detection** — portable users get a ZIP + robocopy update; installed users get a silent installer

### Plugin system
Plugins are resolved in priority order; the first successful plugin stops the chain.

| Plugin | Handles | Quality |
|---|---|---|
| **SocialMedia** | any `http(s)://` URL (Instagram, TikTok, Twitter/X, Reddit, Facebook, …) | yt-dlp, per-chat quality |
| **YouTube** | `https://youtu.be/` or `https://www.youtube.com/` | yt-dlp, per-chat quality |
| **Torrent** | `magnet:` links | MonoTorrent |
| **Direct** | Telegram file attachments | native Telegram API |

---

## Prerequisites

| Requirement | Version |
|---|---|
| Windows 10 / 11 | any |
| .NET 8 runtime | 8.0+ |
| .NET 8 SDK (to build) | 8.0+ |

**yt-dlp** is downloaded and kept up to date automatically. You can also place `yt-dlp.exe` manually in `%APPDATA%\TelegramAutoDownload\tools\`.

---

## Installation

### Option A — Installer (recommended)
Download `TelegramAutoDownload_vX.X.X_Setup.exe` from the [latest release](https://github.com/il90il90/TelegramAutoDownload/releases/latest) and run it.

### Option B — Portable ZIP
Download `TelegramAutoDownload_vX.X.X_Portable.zip`, extract anywhere, and run `TelegramAutoDownload.exe`.

---

## Building from source

### 1. Clone

```bash
git clone https://github.com/il90il90/TelegramAutoDownload.git
cd TelegramAutoDownload
```

### 2. Create `.env` (first-time only)

```env
# Telegram API credentials — obtain at https://my.telegram.org
APP_ID=12345678
API_HASH=abcdef1234567890abcdef1234567890

# Optional: Telegram bot for download notifications
BOT_TOKEN=123456789:AABBCCDDEEFFaabbccddeeff
CHAT_ID=-1001234567890
```

> `.env` is in `.gitignore` — it is never committed.

### 3. Build

```bash
dotnet build RunnerApp.sln
```

### 4. Run

```bash
dotnet run --project TelegramAutoDownload
```

Or open `RunnerApp.sln` in Visual Studio 2022 and press **F5**.

---

## First run

1. On first launch the **Login** window appears — enter your Telegram phone number and the confirmation code (and 2FA password if enabled).
2. Open **Settings** (gear icon) and set your **download folder**, **App ID**, and **API Hash**.
3. The chat list is populated automatically after login. Check the chats you want to monitor.

---

## UI Reference

### Chat list columns

| Column | Description |
|---|---|
| ☑ | Enables **live monitoring** — every new message in this chat is evaluated for download. |
| ID | Telegram internal chat ID. |
| Name | Display name of the chat / group / channel. |
| Username | Public Telegram `@username`. Empty for private groups. |
| Type | `Channel`, `Group`, or `User`. |
| Members | Participant count (fetched from Telegram on refresh). |
| Start Icon | Emoji reaction sent when a download **starts**. |
| End Icon | Emoji reaction sent when a download **completes**. |
| Download Types | **Videos / Photos / Music / Files** — which media categories are active. |
| Min Size (MB) | Files below this size are ignored. `0` = no minimum. |
| Providers | Which plugins are active: YouTube, Social, Torrent, Direct. |
| Quality | yt-dlp quality for URL-based downloads: `best`, `4K`, `1080p`, `720p`, `480p`, `audio`. |
| Filter | Semicolon-separated regex patterns — files whose name matches are skipped. Example: `\.jpg$; thumb_` |
| Folder | Custom folder template. Tokens: `{Type}`, `{ChatName}`, `{Year}`, `{Month}`, `{Day}`. Empty = default (`Type/ChatName/`). |
| Sync | One-time retroactive sweep — downloads everything in the chat history that matches current settings and hasn't been downloaded yet. |
| Mute | 🔔 / 🔕 — toggles Telegram's own notification setting for this chat via the API. |

### Downloads panel

| Column | Description |
|---|---|
| File | File name. |
| Chat | Source chat. |
| Plugin | Plugin handling the download. |
| Size | Bytes downloaded / total size. |
| Progress | Progress bar + percentage. |
| Speed | Current transfer speed. |
| ETA | Estimated time remaining. |
| Status | `⏳ Queued` → `⬇ Downloading` → `✔ Done` / `✖ Error` / `✖ Timeout` / `✖ Cancelled` |
| ↺ | **Retry** — appears when a download fails; click to re-queue it. |
| ✕ | **Cancel** — available while queued or downloading. |

Active downloads are pinned to the top. Completed/error rows auto-remove after 4 seconds.

### Header strip

| Indicator | Description |
|---|---|
| 📊 This session | Files downloaded and total bytes transferred since launch. |
| 🗄 All-time | Cumulative totals across all sessions (persisted in `stats.json`). |
| ⚡ Total speed | Combined speed of all active downloads. |
| Active count | Number of items currently in the queue. |

### Footer

| Control | Description |
|---|---|
| Version button | Shows current version. Click to check for updates. If already up to date shows "✓ Up to date". If an update is available, shows the update dialog. |
| Connection dot | 🟢 connected / 🔴 disconnected. |
| Open folder | Opens the current download folder in Explorer. |

---

## Settings

Open Settings from the gear icon in the top-right corner.

| Setting | Description |
|---|---|
| **App ID / API Hash** | Telegram API credentials from [my.telegram.org](https://my.telegram.org). Changing these requires re-login. |
| **Notification Bot** | Optional Telegram bot for download notifications. Toggle on/off; configure token, chat ID, and which events trigger a notification. |
| **Dark Mode** | Switches the app theme to dark. |
| **Download Folder** | Where downloaded files are saved (browse to choose). |
| **Parallel Threads** | How many files download simultaneously (1–10). |
| **Quiet Hours** | Enable a daily time window during which no new downloads start. Set From/To in 24-hour format. Supports overnight windows (e.g. 23:00–07:00). Use **Sync** after quiet hours to catch up on missed messages. |
| **Export / Import** | Export settings to JSON (credentials excluded) or import from a previously exported file. |

---

## Closing the app

Clicking the **×** button shows a dialog:

| Choice | Effect |
|---|---|
| **Minimize to Tray** | Hides the window; downloads continue in the background. Double-click the tray icon to restore. |
| **Exit** | Quits the application. In-progress downloads are cancelled. |
| **Cancel** | Dismisses the dialog; the window stays open. |

---

## Data files

All user data is stored in `%APPDATA%\TelegramAutoDownload\`:

| File | Description |
|---|---|
| `config.txt` | Chat list and all settings (JSON). |
| `downloaded_ids.json` | Set of Telegram document IDs already downloaded (deduplication index). |
| `stats.json` | All-time download statistics. |
| `skipped_version.txt` | Version tag the user chose to skip in the update dialog. |
| `session.dat` | WTelegramClient session (login state). |
| `logs/` | Rolling daily log files (kept for 7 days). |
| `tools/yt-dlp.exe` | Auto-managed yt-dlp binary. |

---

## Running tests

```bash
dotnet test RunnerApp.sln
```

The test suite covers:
- Quality format string mapping (yt-dlp)
- Plugin routing (`CanHandle` / priority)
- Resume download (`.part` file stream contract)
- Stale `.part` file cleanup
- Deduplication index concurrency
- History sync pagination correctness
- Sticker / voice message filtering
- URL message detection
- `ConfigFile` save/load round-trip
- `CancellationRegistry` operations
- HTTP downloader plugin (with mocked HTTP)
- **Quiet Hours** — same-day, overnight, disabled
- **DownloadItem.CanRetry** — property change notifications, state transitions
- **FolderTemplate** — token resolution, sanitization, padding
- **IgnoreFileByRegex** — filter string parsing
- **Persistent statistics** — JSON round-trip, atomic increment
- **CloseDialog** — enum values and default

---

## Architecture overview

```
TelegramAutoDownload (WPF, entry point)
├── TelegramClient          — Telegram API layer (WTelegramClient)
│   ├── TelegramApp         — update loop, plugin dispatch, quiet hours
│   ├── FileDownloadIndex   — deduplication index (debounced save)
│   ├── PartFileCleanup     — removes stale .part files on startup
│   ├── QuietHoursHelper    — quiet-hours window logic (testable)
│   └── FolderTemplateHelper— folder template token resolution (testable)
├── BasePlugin              — shared interfaces, Config, YtdlpFormatHelper
├── YoutubePlugin           — yt-dlp YouTube handler
├── SocialMediaPlugin       — yt-dlp generic URL handler
├── TorrentPlugin           — MonoTorrent magnet-link handler
└── DownloadService         — direct Telegram file download plugin
```

---

## License

MIT
