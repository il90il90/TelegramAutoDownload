using System;
using System.Threading;
using System.Threading.Tasks;

namespace TelegramClient
{
    /// <summary>
    /// Serializes WTelegram Client API usage. Concurrent calls (e.g. download + send reaction) can deadlock the client.
    /// </summary>
    internal static class TelegramClientApiGate
    {
        private static readonly SemaphoreSlim Gate = new(1, 1);

        public static async Task RunAsync(Func<Task> action, CancellationToken ct = default)
        {
            await Gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                await action().ConfigureAwait(false);
            }
            finally
            {
                Gate.Release();
            }
        }

        public static async Task<T> RunAsync<T>(Func<Task<T>> action, CancellationToken ct = default)
        {
            await Gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                return await action().ConfigureAwait(false);
            }
            finally
            {
                Gate.Release();
            }
        }
    }
}
