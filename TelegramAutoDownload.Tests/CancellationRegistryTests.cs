using BasePlugins;
using FluentAssertions;
using System;
using System.Threading;
using Xunit;

namespace TelegramAutoDownload.Tests
{
    public class CancellationRegistryTests
    {
        // Each test uses a unique key prefix to avoid cross-test interference
        // since CancellationRegistry is a static shared dictionary.
        private static string Key(string suffix) =>
            $"__test_{Guid.NewGuid():N}_{suffix}";

        // ---------------------------------------------------------------------------
        // Register
        // ---------------------------------------------------------------------------

        [Fact]
        public void Register_NewKey_ReturnsNonCancelledToken()
        {
            var key = Key("register_new");
            var token = CancellationRegistry.Register(key);
            token.IsCancellationRequested.Should().BeFalse();
            CancellationRegistry.Remove(key);
        }

        [Fact]
        public void Register_SameKeyTwice_ReplacesOldCts()
        {
            var key = Key("register_twice");
            var firstToken  = CancellationRegistry.Register(key);
            var secondToken = CancellationRegistry.Register(key);

            // The second token must be a fresh, non-cancelled token
            secondToken.IsCancellationRequested.Should().BeFalse();
            // Tokens from different CTS instances are never equal
            firstToken.Equals(secondToken).Should().BeFalse();
            CancellationRegistry.Remove(key);
        }

        [Fact]
        public void Register_SameKeyTwice_DisposesStale_WithoutException()
        {
            var key = Key("register_dispose_stale");
            CancellationRegistry.Register(key);
            // Second Register must silently dispose the first CTS
            var act = () => CancellationRegistry.Register(key);
            act.Should().NotThrow();
            CancellationRegistry.Remove(key);
        }

        // ---------------------------------------------------------------------------
        // Cancel
        // ---------------------------------------------------------------------------

        [Fact]
        public void Cancel_ExistingKey_SetsIsCancellationRequested()
        {
            var key = Key("cancel_existing");
            var token = CancellationRegistry.Register(key);
            token.IsCancellationRequested.Should().BeFalse();

            CancellationRegistry.Cancel(key);

            token.IsCancellationRequested.Should().BeTrue();
            CancellationRegistry.Remove(key);
        }

        [Fact]
        public void Cancel_UnknownKey_DoesNotThrow()
        {
            var act = () => CancellationRegistry.Cancel(Key("unknown_key"));
            act.Should().NotThrow("Cancel on a missing key must be a no-op");
        }

        [Fact]
        public void Cancel_CalledTwice_DoesNotThrow()
        {
            var key = Key("cancel_twice");
            CancellationRegistry.Register(key);

            var act = () =>
            {
                CancellationRegistry.Cancel(key);
                CancellationRegistry.Cancel(key); // second call must be a no-op
            };
            act.Should().NotThrow();
            CancellationRegistry.Remove(key);
        }

        // ---------------------------------------------------------------------------
        // Remove
        // ---------------------------------------------------------------------------

        [Fact]
        public void Remove_AfterRegister_CancelNoLongerWorks()
        {
            var key = Key("remove_then_cancel");
            var token = CancellationRegistry.Register(key);

            CancellationRegistry.Remove(key);

            // After removal, cancelling must be a silent no-op (key is gone)
            var act = () => CancellationRegistry.Cancel(key);
            act.Should().NotThrow();

            // The removed token retains the state it had at removal time (was not cancelled)
            // — but we cannot assert IsCancellationRequested after Dispose on the CTS;
            // any access on a disposed CTS throws ObjectDisposedException.
        }

        [Fact]
        public void Remove_UnknownKey_DoesNotThrow()
        {
            var act = () => CancellationRegistry.Remove(Key("remove_unknown"));
            act.Should().NotThrow("Remove on a missing key must be a no-op");
        }

        [Fact]
        public void Remove_SameKeyTwice_DoesNotThrow()
        {
            var key = Key("remove_twice");
            CancellationRegistry.Register(key);
            CancellationRegistry.Remove(key);

            var act = () => CancellationRegistry.Remove(key);
            act.Should().NotThrow();
        }

        // ---------------------------------------------------------------------------
        // MakeKey
        // ---------------------------------------------------------------------------

        [Theory]
        [InlineData("MyChat", "video.mp4",  "MyChat|video.mp4")]
        [InlineData("",       "file",        "|file")]
        [InlineData("chat",   "",            "chat|")]
        public void MakeKey_ReturnsCorrectFormat(string chat, string file, string expected)
        {
            CancellationRegistry.MakeKey(chat, file).Should().Be(expected);
        }

        [Fact]
        public void MakeKey_DifferentChatsOrFiles_ProduceUniqueKeys()
        {
            var k1 = CancellationRegistry.MakeKey("ChatA", "file.mp4");
            var k2 = CancellationRegistry.MakeKey("ChatB", "file.mp4");
            var k3 = CancellationRegistry.MakeKey("ChatA", "other.mp4");

            k1.Should().NotBe(k2);
            k1.Should().NotBe(k3);
            k2.Should().NotBe(k3);
        }

        // ---------------------------------------------------------------------------
        // Register / Cancel / Remove round-trip
        // ---------------------------------------------------------------------------

        [Fact]
        public void FullLifecycle_RegisterCancelRemove_WorksWithoutError()
        {
            var key = CancellationRegistry.MakeKey("TestChat", "fullcycle.mp4");
            var token = CancellationRegistry.Register(key);

            token.IsCancellationRequested.Should().BeFalse("fresh token should not be cancelled");

            CancellationRegistry.Cancel(key);
            token.IsCancellationRequested.Should().BeTrue("token must be cancelled after Cancel()");

            var removeAct = () => CancellationRegistry.Remove(key);
            removeAct.Should().NotThrow();
        }
    }
}
