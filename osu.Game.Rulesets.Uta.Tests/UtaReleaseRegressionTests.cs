// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENSE file in the repository root for full licence text.

using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Net.Codecrete.QrCodeGenerator;
using osu.Framework.Bindables;
using osu.Framework.Graphics.Primitives;
using osu.Game.Rulesets.Uta.Configuration;
using osu.Game.Rulesets.Uta.Core;
using osu.Game.Rulesets.Uta.Gameplay;
using osu.Game.Rulesets.Uta.Library;
using osu.Game.Rulesets.Uta.Playback;
using osu.Game.Rulesets.Uta.Queue;
using osu.Game.Rulesets.Uta.Recording;
using osu.Game.Rulesets.Uta.Remote;
using osu.Game.Rulesets.Uta.Scoring;
using osu.Game.Rulesets.Uta.Skinning;
using osu.Game.Rulesets.Uta.Skinning.Lookups;
using osu.Game.Rulesets.Uta.UI.HUD;
using osu.Game.Rulesets.Uta.UI.HUD.Pitch;

namespace osu.Game.Rulesets.Uta.Tests;

[TestFixture]
public sealed class UtaReleaseRegressionTests
{
    [Test]
    public void TestReturnedGameplayLeaseImmediatelyRestoresGlobalSelectionControl()
    {
        var global = new osu.Framework.Bindables.BindableInt(1);
        LeasedBindable<int> gameplayLease = global.BeginLease(false);
        Bindable<int> sessionCopy = gameplayLease.GetBoundCopy();

        Assert.That(global.Disabled, Is.True);
        Assert.That(gameplayLease.Return(), Is.True);

        global.Value = 2;
        Assert.Multiple(() =>
        {
            Assert.That(global.Disabled, Is.False);
            Assert.That(global.Value, Is.EqualTo(2));
            Assert.That(sessionCopy.Value, Is.EqualTo(1));
            Assert.That(gameplayLease.Return(), Is.False, "Returning the host lease again must remain safe.");
        });
    }

    [Test]
    public void TestBoundCopyOfGameplayLeaseCannotBeReturned()
    {
        var global = new osu.Framework.Bindables.BindableInt(1);
        LeasedBindable<int> gameplayLease = global.BeginLease(false);
        var resolvedCopy = (LeasedBindable<int>)gameplayLease.GetBoundCopy();

        Assert.That(
            () => resolvedCopy.Return(),
            Throws.InvalidOperationException.With.Message.Contains("original leased source"));
        Assert.That(global.Disabled, Is.True);

        Assert.That(gameplayLease.Return(), Is.True);
        global.Value = 2;
        Assert.That(global.Disabled, Is.False);
        Assert.That(global.Value, Is.EqualTo(2));
    }

    [Test]
    public async Task TestDisposedGameplayServicesStayAvailableForSongSelect()
    {
        using var queue = new UtaSongQueueService();
        var sessions = new UtaGameplaySessionRegistry();
        var router = new UtaRemoteCommandRouter(queue, sessions, new osu.Framework.Bindables.BindableBool());
        var library = new UtaSongLibrary();
        var playback = new UtaPlaybackCoordinator(queue, library, sessions);

        IDisposable lease = router.AttachGameplayServices(library, playback);
        lease.Dispose();

        UtaRemoteCommandResult libraryResult = await router.ExecuteAsync(
            new UtaRemoteCommand(1, UtaRemoteCommands.LibrarySearch, null, null, string.Empty),
            CancellationToken.None);
        UtaRemoteCommandResult playbackResult = await router.ExecuteAsync(
            new UtaRemoteCommand(2, UtaRemoteCommands.SkipToNext, null, null, null),
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(libraryResult.Accepted, Is.True);
            Assert.That(playbackResult.Accepted, Is.False);
            Assert.That(playbackResult.Error, Is.EqualTo("Playback is not ready."));
        });

        playback.Dispose();
        library.Dispose();
    }

    [Test]
    public void TestHistoricalConfigKeysRemainStable()
    {
        int[] values = Enum.GetValues<UtaRulesetSetting>().Select(setting => (int)setting).ToArray();
        Assert.That(values, Is.EqualTo(Enumerable.Range(0, 41)), "Persisted 0.8 keys 0-31 must remain stable and HUD keys must only append.");
        Assert.That((int)UtaRulesetSetting.PitchHudSize, Is.EqualTo(32));
        Assert.That((int)UtaRulesetSetting.HudSafeAreaPadding, Is.EqualTo(39));
        Assert.That((int)UtaRulesetSetting.OriginalVocalsEnabled, Is.EqualTo(40));
    }

    [TestCase(1280, UtaHudDensity.Wide, 180, 56)]
    [TestCase(1279, UtaHudDensity.Standard, 168, 32)]
    [TestCase(840, UtaHudDensity.Standard, 168, 32)]
    [TestCase(839, UtaHudDensity.Compact, 156, 20)]
    [TestCase(560, UtaHudDensity.Compact, 156, 20)]
    [TestCase(559, UtaHudDensity.Narrow, 144, 12)]
    public void TestHudLayoutDensityBoundaries(float width, UtaHudDensity density, float pitchHeight, float padding)
    {
        UtaHudLayoutSnapshot layout = UtaHudLayoutCoordinator.Calculate(width, 720);
        Assert.Multiple(() =>
        {
            Assert.That(layout.Density, Is.EqualTo(density));
            Assert.That(layout.PitchBounds.Height, Is.EqualTo(pitchHeight));
            Assert.That(layout.SafeAreaPadding, Is.EqualTo(padding));
            Assert.That(layout.PitchBounds.Left, Is.EqualTo(padding));
            Assert.That(layout.PitchBounds.Right, Is.EqualTo(width - padding));
        });
    }

    [Test]
    public void TestHudVisibilityAndLyricsAvoidanceRemainIndependent()
    {
        UtaHudLayoutSnapshot hidePitch = UtaHudLayoutCoordinator.Calculate(840, 720, UtaLyricsPosition.Top, showPitch: false);
        UtaHudLayoutSnapshot hideLyrics = UtaHudLayoutCoordinator.Calculate(840, 720, showLyrics: false);
        UtaHudLayoutSnapshot practice = UtaHudLayoutCoordinator.Calculate(840, 720, showPractice: true);

        Assert.Multiple(() =>
        {
            Assert.That(hidePitch.PitchBounds, Is.EqualTo(RectangleF.Empty));
            Assert.That(hidePitch.LyricsBounds.Top, Is.GreaterThan(hidePitch.SafeAreaPadding));
            Assert.That(hideLyrics.LyricsBounds, Is.EqualTo(RectangleF.Empty));
            Assert.That(hideLyrics.PitchBounds, Is.Not.EqualTo(RectangleF.Empty));
            Assert.That(practice.LyricsBounds.Bottom, Is.LessThanOrEqualTo(practice.PracticeBounds.Top - 12));
        });
    }

    [Test]
    public void TestPitchTimelineGeometryPreservesGameplayContract()
    {
        UtaPitchTargetGeometry target = UtaPitchTimelineGeometry.Target(10000, 11000, 60, 10000, 60, 400);
        Assert.Multiple(() =>
        {
            Assert.That(UtaPitchTimelineGeometry.LOOK_BEHIND, Is.EqualTo(1750));
            Assert.That(UtaPitchTimelineGeometry.LOOK_AHEAD, Is.EqualTo(5250));
            Assert.That(UtaPitchTimelineGeometry.VIEW_SPAN, Is.EqualTo(19));
            Assert.That(UtaPitchTimelineGeometry.PLAYHEAD_POSITION, Is.EqualTo(0.25f));
            Assert.That(target.X, Is.EqualTo(0.25f).Within(0.0001));
            Assert.That(target.Width, Is.EqualTo(1f / 7).Within(0.0001));
            Assert.That(target.Y, Is.EqualTo(0.5f).Within(0.0001));
            Assert.That(target.Visible, Is.True);
        });
    }

    [Test]
    public void TestSkinContractsAppendAndPrismKeepsCriticalCues()
    {
        UtaVisualStyle standard = UtaVisualStyle.Prism();
        UtaVisualStyle reduced = UtaVisualStyle.Prism(UtaHudDensity.Narrow, reducedMotion: true);

        Assert.Multiple(() =>
        {
            Assert.That(Enum.GetValues<UtaSkinComponents>().Select(value => (int)value), Is.EqualTo(Enumerable.Range(0, 9)));
            Assert.That((int)UtaSkinConfiguration.AnimationIntensity, Is.EqualTo(9));
            Assert.That((int)UtaSkinConfiguration.SurfaceColour, Is.EqualTo(10));
            Assert.That(UtaSkinAssetNames.Marker, Is.EqualTo("uta-skin-marker"));
            Assert.That(UtaSkinAssetNames.TargetNote(UtaTargetNoteKind.Golden), Is.EqualTo("uta-target-note-golden"));
            Assert.That(UtaSkinAssetNames.TargetNote(UtaTargetNoteKind.GoldenFreestyle), Is.EqualTo("uta-target-note-golden-freestyle"));
            Assert.That(UtaSkinAssetNames.TargetNote(UtaTargetNoteKind.GoldenRap), Is.EqualTo("uta-target-note-golden-rap"));
            Assert.That(UtaSkinAssetNames.TargetNote(UtaTargetNoteKind.GoldenSpoken), Is.EqualTo("uta-target-note-golden-spoken"));
            Assert.That(UtaSkinAssetNames.Feedback(UtaNoteGrade.Perfect), Is.EqualTo("uta-feedback-perfect"));
            Assert.That(UtaSkinAssetNames.Fault(UtaPitchFault.LowCoverage), Is.EqualTo("uta-fault-coverage"));
            Assert.That(UtaSkinAssetNames.IsKnown(UtaSkinAssetNames.HudPanel), Is.True);
            Assert.That(UtaSkinAssetNames.IsKnown(UtaSkinAssetNames.HudAccent), Is.True);
            Assert.That(UtaSkinAssetNames.IsKnown(UtaSkinAssetNames.ParticleSing), Is.True);
            Assert.That(UtaSkinAssetNames.IsKnown(UtaSkinAssetNames.ParticleScore), Is.True);
            Assert.That(standard.Pitch.Target.A, Is.GreaterThan(0));
            Assert.That(UtaAccessiblePalette.Target.B, Is.GreaterThan(UtaAccessiblePalette.Target.R), "Default target notes stay cool ice-blue, not warning yellow.");
            Assert.That(standard.Pitch.Playhead.A, Is.GreaterThan(0));
            Assert.That(standard.Lyrics.Current.A, Is.GreaterThan(0));
            Assert.That(reduced.Lyrics.CurrentSize, Is.GreaterThanOrEqualTo(UtaHudLayoutCoordinator.MINIMUM_LYRICS_FONT_SIZE));
            Assert.That(reduced.Motion.MaxSingingParticles, Is.Zero);
            Assert.That(reduced.Motion.MaxScoringParticles, Is.Zero);
            Assert.That(reduced.Motion.LyricsTokenPulseMilliseconds, Is.Zero);
        });
    }

    [Test]
    public void TestScoringFeedbackAndParticlesAreMountedInGameplayHud()
    {
        using var layer = new UtaGameplayHudLayer(showPitch: true, showLyrics: true, showScore: true, showPractice: false, showRecording: false);

        Assert.Multiple(() =>
        {
            Assert.That(layer.HasSingingParticles, Is.True);
            Assert.That(layer.HasScoringFeedback, Is.True);
        });
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
    public void TestRememberedDeviceSurvivesStoreReload()
    {
        string path = Path.Combine(Path.GetTempPath(), $"uta-devices-{Guid.NewGuid():N}.json");
        DateTimeOffset now = DateTimeOffset.UtcNow;
        string id;
        string secret;

        try
        {
            using (var store = new UtaRemoteCredentialStore(path))
            {
                UtaRemotePairingTicket ticket = store.IssuePairingTicket(UtaRemoteRole.Controller, now);
                Assert.That(store.TryRedeem(ticket.Token, now, out UtaRemoteSession? session, out string? issued, out string error), Is.True, error);
                id = session!.Id;
                secret = issued!;
            }

            using var reloaded = new UtaRemoteCredentialStore(path);
            Assert.That(reloaded.TryResume(id, secret, now, out UtaRemoteSession? restored), Is.True);
            Assert.That(restored!.Role, Is.EqualTo(UtaRemoteRole.Controller));
        }
        finally
        {
            File.Delete(path);
        }
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
    public void TestPostResultsAdvanceAutoplaysWithImmersiveQueueOrAutoAdvance()
    {
        Assert.That(UtaPlaybackCoordinator.ShouldAutoplayNextSong(true, false), Is.True);
        Assert.That(UtaPlaybackCoordinator.ShouldAutoplayNextSong(false, true), Is.True);
        Assert.That(UtaPlaybackCoordinator.ShouldAutoplayNextSong(true, true), Is.True);
        Assert.That(UtaPlaybackCoordinator.ShouldAutoplayNextSong(false, false), Is.False);
    }

    [Test]
    public void TestUnstartedTransitionIsNotTreatedAsStale()
    {
        DateTimeOffset now = DateTimeOffset.Parse("2026-08-18T13:28:00Z");
        Assert.That(
            UtaPlaybackCoordinator.IsStaleTransition(default, UtaPlaybackTransitionState.WaitingForGameplay, now),
            Is.False);
        Assert.That(
            UtaPlaybackCoordinator.IsStaleTransition(now, UtaPlaybackTransitionState.WaitingForGameplay, now.AddSeconds(1)),
            Is.False);
        Assert.That(
            UtaPlaybackCoordinator.IsStaleTransition(now, UtaPlaybackTransitionState.WaitingForGameplay, now.AddSeconds(5)),
            Is.True);
        Assert.That(
            UtaPlaybackCoordinator.IsStaleTransition(now, UtaPlaybackTransitionState.Reserved, now.AddSeconds(2)),
            Is.True);
        Assert.That(
            UtaPlaybackCoordinator.IsStaleTransition(now, UtaPlaybackTransitionState.Failed, now),
            Is.True);
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
