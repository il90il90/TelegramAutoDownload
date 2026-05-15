using System;
using System.Threading;
using System.Threading.Tasks;

namespace TelegramClient
{
    /// <summary>
    /// Serializes WTelegram Client API usage. Concurrent calls (e.g. download + get dialogs) corrupt the client.
    /// Re-entrant: nested calls on the same async flow do not deadlock (e.g. retry inside a download).
    /// </summary>
    internal static class TelegramClientApiGate
    {
        private static readonly SemaphoreSlim Gate = new(1, 1);
        private static readonly AsyncLocal<int> Depth = new();

        public static async Task RunAsync(Func<Task> action, CancellationToken ct = default)
        {
            if (Depth.Value > 0)
            {
                await action().ConfigureAwait(false);
                return;
            }

            await Gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                Depth.Value++;
                await action().ConfigureAwait(false);
            }
            finally
            {
                Depth.Value--;
                Gate.Release();
            }
        }

        public static async Task<T> RunAsync<T>(Func<Task<T>> action, CancellationToken ct = default)
        {
            if (Depth.Value > 0)
                return await action().ConfigureAwait(false);

            await Gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                Depth.Value++;
                return await action().ConfigureAwait(false);
            }
            finally
            {
                Depth.Value--;
                Gate.Release();
            }
        }
    }
}
