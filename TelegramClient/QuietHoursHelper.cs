using System;
using TelegramAutoDownload.Models;

namespace TelegramClient
{
    /// <summary>
    /// Determines whether a given hour falls inside a configured quiet-hours window.
    /// Extracted from TelegramApp so the logic can be unit-tested without a Client instance.
    /// </summary>
    public static class QuietHoursHelper
    {
        /// <summary>
        /// Returns true when <paramref name="currentHour"/> (0-23) falls inside the quiet window.
        /// When <paramref name="currentHour"/> is null the current local hour is used.
        /// Supports overnight windows: Start=23, End=7 means 23:00–07:00 the next day.
        /// </summary>
        public static bool IsInQuietHours(ConfigParams config, int? currentHour = null)
        {
            if (!config.QuietHoursEnabled) return false;
            int hour  = currentHour ?? DateTime.Now.Hour;
            int start = config.QuietHoursStart;
            int end   = config.QuietHoursEnd;
            return start < end
                ? hour >= start && hour < end   // same-day window  e.g. 09:00–17:00
                : hour >= start || hour < end;  // overnight window e.g. 23:00–07:00
        }
    }
}
