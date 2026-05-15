using System;

namespace TelegramClient
{
    /// <summary>Thrown when the on-disk session is corrupt or expired; session file is deleted automatically.</summary>
    public sealed class TelegramSessionInvalidException : InvalidOperationException
    {
        public TelegramSessionInvalidException(string message, Exception? inner = null)
            : base(message, inner) { }
    }
}
