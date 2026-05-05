# Telegram Auto Download

A WPF desktop application for Windows that automatically monitors Telegram channels, groups, and users and downloads media (videos, photos, music, files) as well as YouTube/social-media links and torrents.

---

## Features

- **Automatic media download** from Telegram channels, groups, and DMs
- **Plugin system**: each message type dispatched to the highest-priority plugin
  - **YouTube** — downloads best-quality MP4 via YoutubeExplode
  - **SocialMedia** — downloads any supported site via yt-dlp (Instagram, TikTok, Twitter, Reddit, …)
  - **Torrent** — downloads magnet links via MonoTorrent
  - **Download** — direct Telegram file download fallback
- **Per-chat provider toggles** — enable/disable each plugin per chat
- **Minimum file size** threshold per chat
- **Ignore patterns** (regex) per chat
- **Emoji reaction** on successful download
- **Active downloads panel** with real-time progress bars
- **Telegram bot notification** on success/warning (HTML formatted)
- **QR code login** + phone/code/password login
- **MahApps.Metro UI** with Telegram blue accent

---

## Prerequisites

| Requirement | Version |
|---|---|
| Windows 10/11 | any |
| .NET 8 (runtime) | 8.0+ |
| .NET 8 SDK (to build) | 8.0+ |
| yt-dlp | placed at `tools\yt-dlp.exe` |

The yt-dlp executable is downloaded automatically during project setup (see below), or you can download it manually from [https://github.com/yt-dlp/yt-dlp/releases/latest](https://github.com/yt-dlp/yt-dlp/releases/latest).

---

## Setup

### 1. Clone the repository

```bash
git clone https://github.com/YOUR_USERNAME/TelegramAutoDownload.git
cd TelegramAutoDownload
```

### 2. Create your `.env` file

Copy the example below to a file named `.env` in the **project root** (same folder as `RunnerApp.sln`):

```env
# Telegram API credentials — get them at https://my.telegram.org
APP_ID=12345678
API_HASH=abcdef1234567890abcdef1234567890

# Telegram bot for notifications (optional)
BOT_TOKEN=123456789:AABBCCDDEEFFaabbccddeeff
CHAT_ID=-1001234567890
```

> `.env` is listed in `.gitignore` — it will never be committed.

### 3. Download yt-dlp

Run in PowerShell from the repo root:

```powershell
New-Item -ItemType Directory -Force tools
Invoke-WebRequest https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe -OutFile tools\yt-dlp.exe
```

### 4. Build

```bash
dotnet build RunnerApp.sln
```

### 5. Run

```bash
dotnet run --project TelegramAutoDownload
```

Or open `RunnerApp.sln` in Visual Studio 2022 and press F5.

---

## Plugin System

Plugins are loaded from `Plugins\` at runtime (one subfolder per plugin DLL). Each plugin implements `IBasePlugin` with a `Priority` property — lower priority value = runs first. When a plugin returns `IsSuccess = true` the chain stops.

| Plugin | Priority | Handles |
|---|---|---|
| SocialMedia | 2 | `http(s)://` not containing `youtu` |
| YouTube | 10 (default) | `https://youtu…` / `https://www.youtu…` |
| Torrent | 3 | `magnet:` links |
| Download | (built-in) | Telegram direct-download fallback |

---

## Configuration

`config.txt` is created automatically on first run. It stores:

- Selected chats and their settings
- Download path
- Per-chat provider toggles (`EnabledPlugins`)
- Thread count (`DownloadThreads`, default 3)

Credentials (`APP_ID`, `API_HASH`) are **not** stored in `config.txt` — they come from `.env`.

---

## Running Tests

```bash
dotnet test RunnerApp.sln
```

---

## License

MIT
