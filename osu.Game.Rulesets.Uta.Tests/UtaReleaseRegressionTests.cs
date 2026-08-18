// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENSE file in the repository root for full licence text.

using System;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Net.Codecrete.QrCodeGenerator;
using osu.Game.Rulesets.Uta.Configuration;
using osu.Game.Rulesets.Uta.Core;
using osu.Game.Rulesets.Uta.Playback;
using osu.Game.Rulesets.Uta.Recording;
using osu.Game.Rulesets.Uta.Remote;

namespace osu.Game.Rulesets.Uta.Tests;

[TestFixture]
public sealed class UtaReleaseRegressionTests
{
    [Test]
    public void TestHistoricalConfigKeysRemainStable()
    {
        Assert.That((int)UtaRulesetSetting.BackgroundMusicVolume, Is.EqualTo(0));
        Assert.That((int)UtaRulesetSetting.MicrophoneOutputDevice, Is.EqualTo(5));
        Assert.That((int)UtaRulesetSetting.ScoreHudPosition, Is.EqualTo(22));
        Assert.That((int)UtaRulesetSetting.RemoteControlPort, Is.GreaterThan(22));
    }

    [Test]
    public void TestControllerCommandParsingAndBounds()
    {
        byte[] command = Encoding.UTF8.GetBytes("{\"type\":\"command\",\"sequence\":1,\"command\":\"speed\",\"value\":1.25}");
        Assert.That(UtaRemoteProtocol.TryParseCommand(command, UtaRemoteRole.Controller, out UtaRemoteCommand? parsed, out string error), Is.True, error);
        Assert.That(parsed, Is.Not.Null);
        Assert.That(parsed!.Number, Is.EqualTo(1.25));

        byte[] invalid = Encoding.UTF8.GetBytes("{\"type\":\"command\",\"sequence\":2,\"command\":\"speed\",\"value\":8}");
        Assert.That(UtaRemoteProtocol.TryParseCommand(invalid, UtaRemoteRole.Controller, out _, out _), Is.False);
    }

    [Test]
    public void TestSpectatorIsReadOnly()
    {
        byte[] command = Encoding.UTF8.GetBytes("{\"type\":\"command\",\"sequence\":1,\"command\":\"play\"}");
        Assert.That(UtaRemoteProtocol.TryParseCommand(command, UtaRemoteRole.Spectator, out _, out string error), Is.False);
        Assert.That(error, Does.Contain("read-only"));
    }

    [Test]
    public void TestPairingTicketIsSingleUseAndReconnectCanBeRevoked()
    {
        using var store = new UtaRemoteCredentialStore();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        UtaRemotePairingTicket ticket = store.IssuePairingTicket(UtaRemoteRole.Controller, now);

        Assert.That(store.TryRedeem(ticket.Token, now, out UtaRemoteSession? session, out string? secret, out string error), Is.True, error);
        Assert.That(session, Is.Not.Null);
        Assert.That(secret, Is.Not.Null.And.Not.Empty);
        Assert.That(store.TryRedeem(ticket.Token, now, out _, out _, out _), Is.False);
        Assert.That(store.TryResume(session!.Id, secret!, now, out _), Is.True);
        Assert.That(store.Revoke(session.Id), Is.True);
        Assert.That(store.TryResume(session.Id, secret!, now, out _), Is.False);
    }

    [Test]
    public void TestExpiredTicketIsRejected()
    {
        using var store = new UtaRemoteCredentialStore();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        UtaRemotePairingTicket ticket = store.IssuePairingTicket(UtaRemoteRole.Spectator, now, TimeSpan.FromSeconds(1));
        Assert.That(store.TryRedeem(ticket.Token, now.AddSeconds(2), out _, out _, out _), Is.False);
    }

    [Test]
    public void TestReplayGuardRejectsDuplicateAndOutOfOrderCommands()
    {
        var guard = new UtaRemoteReplayGuard();
        Assert.That(guard.TryAdvance(1), Is.True);
        Assert.That(guard.TryAdvance(1), Is.False);
        Assert.That(guard.TryAdvance(0), Is.False);
        Assert.That(guard.TryAdvance(3), Is.True);
        Assert.That(guard.TryAdvance(2), Is.False);
    }

    [TestCase("127.0.0.1", true)]
    [TestCase("10.1.2.3", true)]
    [TestCase("172.16.0.1", true)]
    [TestCase("172.32.0.1", false)]
    [TestCase("192.168.20.4", true)]
    [TestCase("8.8.8.8", false)]
    public void TestPrivateNetworkPolicy(string address, bool expected)
        => Assert.That(UtaRemoteNetworkPolicy.IsPrivateOrLoopback(IPAddress.Parse(address)), Is.EqualTo(expected));

    [Test]
    public async Task TestPcmQueueCompletionAndDisposalAreIdempotent()
    {
        var queue = new UtaPcmCaptureQueue(2);
        Assert.That(queue.TryWrite(new float[] { 0.1f, -0.1f, 0.2f, -0.2f }, 48_000, 2, 123, 1), Is.True);
        UtaPcmCaptureBlock? block = await queue.ReadAsync(CancellationToken.None);
        Assert.That(block, Is.Not.Null);
        Assert.That(block!.FrameCount, Is.EqualTo(2));
        block.Dispose();
        queue.Complete();
        queue.Complete();
        await queue.DisposeAsync();
        await queue.DisposeAsync();
        Assert.That(queue.IsCompleted, Is.True);
        Assert.That(queue.QueuedFrames, Is.EqualTo(0));
    }

    [Test]
    public void TestAutoplayFactoryProducesFormalVoicedAndSilentFrames()
    {
        var note = new UtaNote
        {
            StartTime = 0,
            Duration = 1000,
            Midi = 60,
        };

        var voiced = UtaAutoplayFrameFactory.Create(note, 2, 100);
        Assert.That(voiced.Hertz, Is.Not.Null);
        Assert.That(voiced.Clarity, Is.EqualTo(1));
        Assert.That(voiced.WindowDurationMilliseconds, Is.EqualTo(UtaAutoplayFrameFactory.FRAME_DURATION_MILLISECONDS));

        var silent = UtaAutoplayFrameFactory.Create(null, 0, 200);
        Assert.That(silent.Hertz, Is.Null);
        Assert.That(silent.Clarity, Is.EqualTo(0));
    }

    [Test]
    public void TestPostResultsAdvanceAutoplaysOnlyWithImmersiveQueue()
    {
        Assert.That(UtaPlaybackCoordinator.ShouldAutoplayNextSong(true), Is.True);
        Assert.That(UtaPlaybackCoordinator.ShouldAutoplayNextSong(false), Is.False);
    }

    [TestCase("http://192.168.1.42:27835/#ticket=aB3dEfGhIjKlMnOpQrStUvWxYz012345&role=controller")]
    [TestCase("http://192.168.1.42:27835/#ticket=aB3dEfGhIjKlMnOpQrStUvWxYz012345678901234567890123456789012345&role=spectator")]
    public void TestPairingUrlEncodesToScannableQrCode(string pairingUrl)
    {
        // Regression guard for UtaRemoteQrDisplay: every QR Model 2 symbol carries this exact
        // 7x7 finder pattern in its top-left corner, independent of size/content/mask, so a
        // corrupted encoder (bad Reed-Solomon, wrong mask, off-by-one drawing) would break this.
        bool[,] expectedFinderPattern =
        {
            { true, true, true, true, true, true, true },
            { true, false, false, false, false, false, true },
            { true, false, true, true, true, false, true },
            { true, false, true, true, true, false, true },
            { true, false, true, true, true, false, true },
            { true, false, false, false, false, false, true },
            { true, true, true, true, true, true, true },
        };

        QrCode qr = QrCode.EncodeText(pairingUrl, QrCode.Ecc.Medium);

        Assert.That(qr.Size, Is.InRange(21, 177));
        Assert.That((qr.Size - 17) % 4, Is.EqualTo(0));

        for (int y = 0; y < 7; y++)
            for (int x = 0; x < 7; x++)
                Assert.That(qr.GetModule(x, y), Is.EqualTo(expectedFinderPattern[y, x]), $"finder module ({x},{y})");
    }
}
