## What's new in v2.2.0

### Bug Fixes
- **Download Cancellation (DownladerPlugin)**: The cancel button now correctly stops URL downloads, deletes partial files, and reports errors properly.
- **Videos.cs**: Removed redundant nested try/catch for cleaner error handling.

### New Features
- **Auto Retry**: All downloads automatically retry up to 3 times on failure (2s then 5s delay).
- **System Tray**: The app minimizes to the system tray when you close the window. Double-click to restore.
- **Enhanced Completion Notification**: Telegram bot messages now include file size, download duration, and average speed.
- **Per-Chat Date Filter**: Add an "After Date" to any chat so only newer messages trigger downloads.
- **Auto Update**: The app checks GitHub for new releases on startup and offers to install them automatically.

### UI Improvements
- **Dark Mode**: Toggle in Settings, persisted between sessions.
- **Session Statistics**: Live stats strip in the header (files downloaded, total bytes, active downloads).

### Infrastructure
- **Logging**: Serilog rolling daily logs saved to `logs/app-.log` (7-day retention).
- **yt-dlp Auto Update**: Checks GitHub on startup and updates `yt-dlp.exe` silently if a newer version is available.
- **Export / Import Settings**: Export settings to JSON (API credentials excluded for security).

---

### Installation
1. Download `TelegramAutoDownload_v2.2.0.zip`
2. Extract to any folder
3. Run `TelegramAutoDownload.exe`
4. Requires [.NET 8 Runtime](https://dotnet.microsoft.com/download/dotnet/8.0)
