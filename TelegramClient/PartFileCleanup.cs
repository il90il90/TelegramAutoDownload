using System;
using System.IO;

namespace TelegramClient
{
    /// <summary>
    /// Removes stale .part files left behind by interrupted downloads.
    /// .part files older than <see cref="DefaultMaxAgeDays"/> days are deleted on startup
    /// so the download folder does not accumulate half-finished remnants indefinitely.
    /// Recent .part files are kept so the resume feature can pick them up on the next attempt.
    /// </summary>
    public static class PartFileCleanup
    {
        /// <summary>
        /// .part files younger than this threshold are kept for potential resume.
        /// Older ones are treated as abandoned and deleted.
        /// </summary>
        public const int DefaultMaxAgeDays = 7;

        /// <summary>
        /// Scans <paramref name="rootPath"/> recursively and deletes .part files whose
        /// last-write timestamp is older than <paramref name="maxAgeDays"/> days.
        /// Returns the number of files deleted.
        /// Non-critical: all exceptions are swallowed so a permission error on one file
        /// never blocks startup.
        /// </summary>
        public static int CleanStaleParts(string rootPath, int maxAgeDays = DefaultMaxAgeDays)
        {
            if (!Directory.Exists(rootPath)) return 0;

            var cutoff = DateTime.UtcNow.AddDays(-maxAgeDays);
            int deleted = 0;

            try
            {
                foreach (var file in Directory.EnumerateFiles(rootPath, "*.part", SearchOption.AllDirectories))
                {
                    try
                    {
                        if (File.GetLastWriteTimeUtc(file) < cutoff)
                        {
                            File.Delete(file);
                            deleted++;
                        }
                    }
                    catch { /* skip files that cannot be deleted (locked, permissions, etc.) */ }
                }
            }
            catch { /* directory access error — skip entire scan */ }

            return deleted;
        }
    }
}
