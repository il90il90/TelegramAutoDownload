using BasePlugins;
using FluentAssertions;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace TelegramAutoDownload.Tests
{
    /// <summary>
    /// Tests for the Cancel All feature.
    ///
    /// Covers:
    ///   - CancellationRegistry.CancelAll() cancels every registered token
    ///   - CancelAll() is a no-op when the registry is empty
    ///   - CancelAll() handles already-cancelled tokens without throwing
    ///   - New registrations after CancelAll() get fresh, un-cancelled tokens
    ///   - CancelAll() is safe to call concurrently
    ///   - Key resolution logic: CancellationKey match vs fall-back to chatName|fileName
    ///   - Bug scenario: token registered before CancellationKey written to UI item
    /// </summary>
    public class CancelAllTests
    {
        // Unique prefix so tests don't collide with each other in the shared static registry
        private static string K(string s) => $"__cancelall_{Guid.NewGuid():N}_{s}";

        // ── CancelAll basics ──────────────────────────────────────────────────────

        [Fact]
        public void CancelAll_NoTokensRegistered_DoesNotThrow()
        {
            // Registry may have tokens from other tests, but CancelAll must never throw
            var act = () => CancellationRegistry.CancelAll();
            act.Should().NotThrow("CancelAll on an empty or populated registry must always be safe");
        }

        [Fact]
        public void CancelAll_OneToken_CancelsIt()
        {
            var key   = K("one");
            var token = CancellationRegistry.Register(key);
            token.IsCancellationRequested.Should().BeFalse();

            CancellationRegistry.CancelAll();

            token.IsCancellationRequested.Should().BeTrue(
                "CancelAll must cancel every registered token regardless of key");

            CancellationRegistry.Remove(key);
        }

        [Fact]
        public void CancelAll_MultipleTokens_CancelsAll()
        {
            var keys   = new[] { K("a"), K("b"), K("c"), K("d") };
            var tokens = new List<CancellationToken>();
            foreach (var k in keys)
                tokens.Add(CancellationRegistry.Register(k));

            CancellationRegistry.CancelAll();

            tokens.Should().AllSatisfy(t =>
                t.IsCancellationRequested.Should().BeTrue(
                    "every token in the registry must be cancelled by CancelAll"));

            foreach (var k in keys) CancellationRegistry.Remove(k);
        }

        [Fact]
        public void CancelAll_AlreadyCancelledTokens_DoesNotThrow()
        {
            var key = K("already_cancelled");
            CancellationRegistry.Register(key);
            CancellationRegistry.Cancel(key); // cancel once manually

            // Second cancel via CancelAll must be a no-op
            var act = () => CancellationRegistry.CancelAll();
            act.Should().NotThrow();

            CancellationRegistry.Remove(key);
        }

        [Fact]
        public void CancelAll_ThenRegisterNew_NewTokenIsNotCancelled()
        {
            var existingKey = K("existing");
            var existingToken = CancellationRegistry.Register(existingKey);

            CancellationRegistry.CancelAll();

            existingToken.IsCancellationRequested.Should().BeTrue();

            // New registration after CancelAll must be fresh
            var newKey   = K("fresh_after_cancelall");
            var newToken = CancellationRegistry.Register(newKey);
            newToken.IsCancellationRequested.Should().BeFalse(
                "Register after CancelAll must produce a new, un-cancelled token");

            CancellationRegistry.Remove(existingKey);
            CancellationRegistry.Remove(newKey);
        }

        // ── CancelAll + Count ─────────────────────────────────────────────────────

        [Fact]
        public void Count_AfterRegister_IncrementsCorrectly()
        {
            var before = CancellationRegistry.Count;

            var k1 = K("count1");
            var k2 = K("count2");
            CancellationRegistry.Register(k1);
            CancellationRegistry.Register(k2);

            CancellationRegistry.Count.Should().Be(before + 2);

            CancellationRegistry.Remove(k1);
            CancellationRegistry.Remove(k2);
        }

        // ── Concurrency safety ────────────────────────────────────────────────────

        [Fact]
        public async Task CancelAll_ConcurrentWithRegister_DoesNotThrow()
        {
            // Simulate multiple threads registering tokens while CancelAll fires
            var exceptions = new System.Collections.Concurrent.ConcurrentBag<Exception>();

            var tasks = new List<Task>();
            for (int i = 0; i < 20; i++)
            {
                var idx = i;
                tasks.Add(Task.Run(() =>
                {
                    try
                    {
                        var k = K($"concurrent_{idx}");
                        CancellationRegistry.Register(k);
                        CancellationRegistry.CancelAll();
                        CancellationRegistry.Remove(k);
                    }
                    catch (Exception ex) { exceptions.Add(ex); }
                }));
            }

            await Task.WhenAll(tasks);

            exceptions.Should().BeEmpty("CancelAll must be thread-safe with concurrent Register/Remove calls");
        }

        // ── Key-resolution logic (Bug B regression) ──────────────────────────────

        /// <summary>
        /// Simulates the Bug B scenario: a download is "Downloading" but CancellationKey is
        /// not yet written to the DownloadItem (because OnProgress hasn't fired yet).
        /// CancelAll() must still cancel the token because it iterates all registered tokens,
        /// not just the ones whose keys are stored on UI items.
        /// </summary>
        [Fact]
        public void BugB_TokenRegisteredBeforeCancellationKeyAssigned_CancelAllStillCancels()
        {
            // The plugin/download task registers the token with the real filename…
            var realFileName = "real_video_2026.mp4";
            var chatName = "TestChat";
            var realKey  = CancellationRegistry.MakeKey(chatName, realFileName);
            var token    = CancellationRegistry.Register(realKey);

            // …but the UI item still has the placeholder name and CancellationKey is empty.
            // In the old code, CancelAllDownloads would compute:
            //   key = MakeKey(chatName, "file_1234") — which doesn't exist in the registry.
            // CancelAll() bypasses this: it cancels ALL tokens, including realKey.
            CancellationRegistry.CancelAll();

            token.IsCancellationRequested.Should().BeTrue(
                "CancelAll must cancel the token even if the UI item's CancellationKey is still empty");

            CancellationRegistry.Remove(realKey);
        }

        /// <summary>
        /// Simulates the scenario where a plugin (YouTube/SocialMedia) registers its token
        /// with `MakeKey(chatName, tempName)` where tempName is a URL fragment.
        /// CancelAll must cancel this token too.
        /// </summary>
        [Fact]
        public void PluginDownload_TokenWithTempName_CancelAllCancelsIt()
        {
            var chatName = "MyChannel";
            var tempName = "https://youtu.be/dQw4w9WgXcQ"; // URL as the plugin's tempName
            var key  = CancellationRegistry.MakeKey(chatName, tempName);
            var token = CancellationRegistry.Register(key);

            CancellationRegistry.CancelAll();

            token.IsCancellationRequested.Should().BeTrue(
                "plugin download tokens must be cancelled by CancelAll");

            CancellationRegistry.Remove(key);
        }

        // ── Multiple independent downloads — all get cancelled ────────────────────

        [Fact]
        public void CancelAll_MixedDownloads_AllTokensCancelled()
        {
            // Simulate 3 native downloads + 2 plugin downloads
            var nativeKeys  = new[] { K("native1"), K("native2"), K("native3") };
            var pluginKeys  = new[] { K("yt_url1"),  K("sm_url1") };

            var allTokens = new List<CancellationToken>();
            foreach (var k in nativeKeys) allTokens.Add(CancellationRegistry.Register(k));
            foreach (var k in pluginKeys) allTokens.Add(CancellationRegistry.Register(k));

            CancellationRegistry.CancelAll();

            allTokens.Should().AllSatisfy(t =>
                t.IsCancellationRequested.Should().BeTrue());

            foreach (var k in nativeKeys) CancellationRegistry.Remove(k);
            foreach (var k in pluginKeys) CancellationRegistry.Remove(k);
        }

        // ── Full lifecycle: Register → CancelAll → Remove ─────────────────────────

        [Fact]
        public void FullLifecycle_RegisterCancelAllRemove_WorksWithoutError()
        {
            var keys   = new[] { K("lc1"), K("lc2") };
            var tokens = new List<CancellationToken>();
            foreach (var k in keys)
                tokens.Add(CancellationRegistry.Register(k));

            CancellationRegistry.CancelAll();
            tokens.Should().AllSatisfy(t => t.IsCancellationRequested.Should().BeTrue());

            var removeAct = () => { foreach (var k in keys) CancellationRegistry.Remove(k); };
            removeAct.Should().NotThrow();
        }

        // ── CancelAll does not remove tokens — Remove still needed ───────────────

        [Fact]
        public void CancelAll_DoesNotRemoveTokens_RemoveStillCleansUp()
        {
            var key = K("still_present_after_cancelall");
            CancellationRegistry.Register(key);

            var countBefore = CancellationRegistry.Count;
            CancellationRegistry.CancelAll();
            // Count should remain the same — CancelAll does not Remove
            CancellationRegistry.Count.Should().Be(countBefore,
                "CancelAll must not remove tokens; they stay until the download task calls Remove()");

            CancellationRegistry.Remove(key);
        }
    }
}
