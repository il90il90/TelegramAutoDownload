using FluentAssertions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TelegramClient;
using Xunit;

namespace TelegramAutoDownload.Tests
{
    /// <summary>
    /// Thread-safety and correctness tests for the static FileDownloadIndex.
    /// Because FileDownloadIndex is a shared static, tests use IDs in a large,
    /// unique numeric range (negative values, or values far above typical Telegram IDs)
    /// so they don't collide with real downloaded-ids.json data or other tests.
    /// </summary>
    public class FileDownloadIndexConcurrencyTests
    {
        // Use a per-test base offset so tests running in parallel do not share IDs
        private static long NextBase() => -(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + Random.Shared.NextInt64(1, 1_000_000));

        // ---------------------------------------------------------------------------
        // Basic single-threaded correctness
        // ---------------------------------------------------------------------------

        [Fact]
        public void MarkDownloaded_ImmediatelyReflectedInMemory()
        {
            long id = NextBase();
            FileDownloadIndex.IsAlreadyDownloaded(id).Should().BeFalse("precondition");

            FileDownloadIndex.MarkDownloaded(id);
            FileDownloadIndex.IsAlreadyDownloaded(id).Should().BeTrue("must be visible in memory before disk flush");

            FileDownloadIndex.Remove(id);
        }

        [Fact]
        public void MarkDownloaded_Twice_DoesNotThrow()
        {
            long id = NextBase();
            var act = () =>
            {
                FileDownloadIndex.MarkDownloaded(id);
                FileDownloadIndex.MarkDownloaded(id); // idempotent
            };
            act.Should().NotThrow();
            FileDownloadIndex.Remove(id);
        }

        [Fact]
        public void Remove_ResetsIsAlreadyDownloaded()
        {
            long id = NextBase();
            FileDownloadIndex.MarkDownloaded(id);
            FileDownloadIndex.IsAlreadyDownloaded(id).Should().BeTrue("precondition");

            FileDownloadIndex.Remove(id);
            FileDownloadIndex.IsAlreadyDownloaded(id).Should().BeFalse("Remove must clear the record");
        }

        [Fact]
        public void Remove_UnknownId_DoesNotThrow()
        {
            long id = NextBase();
            var act = () => FileDownloadIndex.Remove(id);
            act.Should().NotThrow("Remove on an ID that was never marked must be a no-op");
        }

        [Fact]
        public void Flush_DoesNotThrow()
        {
            var act = () => FileDownloadIndex.Flush();
            act.Should().NotThrow("Flush on a clean (non-dirty) index must be a no-op");
        }

        // ---------------------------------------------------------------------------
        // Concurrent writes (thread-safety)
        // ---------------------------------------------------------------------------

        [Fact]
        public void MarkDownloaded_ParallelWrites_NoConcurrencyErrors()
        {
            const int threads = 50;
            const int perThread = 100;
            long baseId = Math.Abs(NextBase()); // use positive range to be safe
            // Use IDs in the range [baseId, baseId + threads*perThread)
            var ids = Enumerable.Range(0, threads * perThread)
                                .Select(i => baseId + i)
                                .ToList();

            var act = () =>
                Parallel.ForEach(ids, id => FileDownloadIndex.MarkDownloaded(id));

            act.Should().NotThrow("concurrent MarkDownloaded calls must never throw");

            // All IDs must be visible after all writes complete
            ids.All(FileDownloadIndex.IsAlreadyDownloaded)
               .Should().BeTrue("every ID written in parallel must be visible after completion");

            // Cleanup (Remove each ID to leave shared state clean)
            Parallel.ForEach(ids, id => FileDownloadIndex.Remove(id));
        }

        [Fact]
        public async Task MarkDownloaded_ConcurrentWithIsAlreadyDownloaded_DoesNotDeadlock()
        {
            const int count = 200;
            long baseId = Math.Abs(NextBase());
            var ids = Enumerable.Range(0, count).Select(i => baseId + i).ToList();

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

            // Writers and readers run concurrently
            var writerTask = Task.Run(() =>
            {
                foreach (var id in ids)
                    FileDownloadIndex.MarkDownloaded(id);
            }, cts.Token);

            var readerTask = Task.Run(() =>
            {
                for (int i = 0; i < count * 3; i++)
                    _ = FileDownloadIndex.IsAlreadyDownloaded(baseId + (i % count));
            }, cts.Token);

            await Task.WhenAll(writerTask, readerTask);

            cts.IsCancellationRequested.Should().BeFalse("tasks must finish well within the timeout (no deadlock)");

            // Cleanup
            foreach (var id in ids) FileDownloadIndex.Remove(id);
        }

        [Fact]
        public async Task Remove_ConcurrentWithMark_DoesNotCorruptState()
        {
            const int n = 100;
            long baseId = Math.Abs(NextBase());
            var ids = Enumerable.Range(0, n).Select(i => baseId + i).ToList();

            // Pre-mark all IDs
            foreach (var id in ids) FileDownloadIndex.MarkDownloaded(id);

            // Mark new IDs concurrently while removing the pre-marked ones
            long newBase = baseId + n + 1;
            var newIds = Enumerable.Range(0, n).Select(i => newBase + i).ToList();

            await Task.WhenAll(
                Task.Run(() => { foreach (var id in ids)    FileDownloadIndex.Remove(id); }),
                Task.Run(() => { foreach (var id in newIds) FileDownloadIndex.MarkDownloaded(id); })
            );

            // New IDs must all be present
            newIds.All(FileDownloadIndex.IsAlreadyDownloaded)
                  .Should().BeTrue("newly marked IDs must survive concurrent removals of other IDs");

            // Old IDs must be gone
            ids.Any(FileDownloadIndex.IsAlreadyDownloaded)
               .Should().BeFalse("removed IDs must no longer be present");

            // Cleanup
            foreach (var id in newIds) FileDownloadIndex.Remove(id);
        }

        // ---------------------------------------------------------------------------
        // Idempotency under load
        // ---------------------------------------------------------------------------

        [Fact]
        public void MarkDownloaded_SameIdFromManyThreads_CountsOnce()
        {
            long id = Math.Abs(NextBase());
            const int threads = 20;

            Parallel.For(0, threads, _ => FileDownloadIndex.MarkDownloaded(id));

            // Must be present exactly once (HashSet dedup)
            FileDownloadIndex.IsAlreadyDownloaded(id).Should().BeTrue();

            FileDownloadIndex.Remove(id);
            FileDownloadIndex.IsAlreadyDownloaded(id).Should().BeFalse("after one Remove the ID is gone");
        }
    }
}
