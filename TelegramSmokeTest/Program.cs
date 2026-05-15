using Newtonsoft.Json;
using TelegramAutoDownload.Models;
using TelegramClient;
using TelegramClient.Models;
using TL;

/// <summary>
/// Live smoke test against the user's saved session (run when no other app instance holds session.dat).
/// Exit 0 = all checks passed.
/// </summary>
var configPath = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
    "TelegramAutoDownload", "config.txt");

if (!File.Exists(configPath))
{
    Console.Error.WriteLine("FAIL: config.txt not found in AppData.");
    return 1;
}

var config = JsonConvert.DeserializeObject<ConfigParams>(File.ReadAllText(configPath))
             ?? new ConfigParams();
if (config.AppId == 0 || string.IsNullOrEmpty(config.ApiHash))
{
    Console.Error.WriteLine("FAIL: AppId/ApiHash missing in config.");
    return 1;
}

Console.WriteLine("Connecting to Telegram (saved session)…");
var app = new TelegramApp(config.AppId, config.ApiHash);
await app.WaitForLoginAsync(60_000);

try
{
    if (!await app.EnsureTelegramReadyAsync())
    {
        Console.Error.WriteLine("FAIL: Not logged in. Open Telegram Auto Download once and sign in (phone code).");
        return 2;
    }
}
catch (Exception ex) when (TelegramSessionHelper.IsAuthKeyError(ex))
{
    TelegramSessionHelper.DeleteSessionFile();
    Console.Error.WriteLine("FAIL: Corrupt session removed. Open the app and sign in once.");
    return 2;
}

Console.WriteLine($"OK: Logged in as user id {app.Client.UserId}");

IList<TelegramClient.Models.ChatDto> chats;
try
{
    chats = await app.GetAllChats();
}
catch (TelegramSessionInvalidException ex)
{
    Console.Error.WriteLine($"FAIL: {ex.Message}");
    return 2;
}
Console.WriteLine($"OK: Loaded {chats.Count} chats from Telegram.");

if (chats.Count == 0)
{
    Console.Error.WriteLine("FAIL: Chat list empty.");
    return 3;
}

var sample = chats.FirstOrDefault(c => c.Type is "Channel" or "Group") ?? chats[0];
var (messages, _, hasMore, error) = await app.FetchBrowseHistoryPageAsync(sample, 0, 20);
if (!string.IsNullOrEmpty(error))
{
    Console.Error.WriteLine($"FAIL: Browse '{sample.Name}': {error}");
    return 4;
}

Console.WriteLine($"OK: Browse '{sample.Name}' — {messages.Count} messages (hasMore={hasMore}).");
Console.WriteLine("Smoke test passed.");
return 0;
