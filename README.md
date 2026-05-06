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

## UI Reference

### Chat List Columns

| Column | Description |
|---|---|
| ☑ (checkbox) | Enables **live monitoring** for this chat. When checked, every new message that arrives is automatically evaluated for download. |
| ID | Telegram internal chat/channel/user ID. |
| Name | Display name of the chat, group, or channel. |
| Username | Public Telegram username (e.g. `@channelname`). Empty for private groups. |
| Type | `Channel`, `Group`, or `User`. |
| Members | Number of members/participants in the group or channel, fetched from Telegram on refresh. |
| Start Icon | Emoji reaction sent on the Telegram message **when a download starts**. Opened from the dropdown; options come from the reactions available in that specific chat. |
| End Icon | Emoji reaction sent on the Telegram message **when a download completes successfully**. Useful to visually mark processed messages inside Telegram. |
| Download Types | Which media categories are monitored: **Videos**, **Photos**, **Music**, **Files**. Only the selected types are downloaded. |
| Min Size Download (MB) | Files smaller than this value (in megabytes) are ignored. Set to `0` to download everything regardless of size. |
| Providers | Which download plugins are active for this chat (see [Plugin System](#plugin-system) below). |
| Sync | Scans the **full history** of the chat and downloads every file that matches the current settings and hasn't been downloaded yet. Different from live monitoring — this is a one-time retroactive sweep. |
| Mute | 🔔 = notifications active / 🔕 = muted. Clicking toggles Telegram's notification setting for this chat directly via the API. This affects notifications inside the Telegram app itself, not just this application. |

### Providers (per chat)

| Provider | What it does |
|---|---|
| **YouTube** | Detects `https://youtu.be/` or `https://www.youtube.com/` links in messages and downloads the best-quality MP4 using YoutubeExplode. |
| **Social** | Detects any other `http(s)://` link and passes it to **yt-dlp**, which supports Instagram, TikTok, Twitter/X, Reddit, Facebook, and hundreds of other platforms. |
| **Torrent** | Detects `magnet:` links and downloads the torrent content via MonoTorrent. |
| **Direct** | Downloads the raw Telegram file attachment directly from Telegram's servers. This is the fallback for all content that is not a link — e.g. videos, photos, audio files, and documents sent as attachments. |

Plugins run in priority order (SocialMedia → Torrent → YouTube → Direct). The first plugin that successfully handles a message stops the chain.

### Downloads Panel

The bottom panel shows all in-progress and queued downloads in real time.

| Column | Description |
|---|---|
| File | Name of the file being downloaded. |
| Chat | The chat/channel the file came from. |
| Plugin | Which plugin is handling the download. |
| Size | Bytes downloaded so far / total file size. |
| Progress | Visual progress bar with percentage. |
| Speed | Current download speed for this individual file. |
| ETA | Estimated time remaining based on current speed. |
| Status | `⏳ Queued` → `⬇ Downloading` → `✔ Done` / `✖ Error` / `✖ Cancelled`. |

The **⚡ total speed** badge in the panel header shows the combined download speed of all active downloads.

Active downloads always appear at the top; completed/errored rows auto-remove after 4 seconds.

---

## Running Tests

```bash
dotnet test RunnerApp.sln
```

---

## License

MIT
