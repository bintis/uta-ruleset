// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENSE file in the repository root for full licence text.

using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using osu.Game.Rulesets.Uta.Recording;
using osu.Game.Rulesets.Uta.Remote;
using osu.Game.Rulesets.Uta.Scoring;

namespace osu.Game.Rulesets.Uta.Tests;

[TestFixture]
public sealed class UtaRuntimeHardeningTests
{
    private string root = null!;

    [SetUp]
    public void SetUp()
    {
        root = Path.Combine(Path.GetTempPath(), $"uta-runtime-hardening-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(root))
            Directory.Delete(root, true);
    }

    [Test]
    public void ScoringMatrixKeepsTransposeOctaveLatencyRateAndLoopEpochDeterministic()
    {
        foreach (int transpose in new[] { -6, 0, 6 })
        foreach (bool octaveFold in new[] { false, true })
        foreach (double rate in new[] { 0.75, 1.0, 1.5 })
        foreach (long latency in new[] { -500_000L, 0L, 500_000L })
        foreach (int epoch in new[] { 0, 1 })
        {
            var mapper = new UtaGameplayTimelineMapper(1_000_000);
            mapper.Reset(1_000_000, 0, rate);
            long anchorTimestamp = epoch == 0 ? 1_000_000 : 2_000_000;
            if (epoch != 0)
                Assert.That(mapper.AddAnchor(anchorTimestamp, 0, rate, startsNewTimelineEpoch: true), Is.EqualTo(epoch));

            UtaScoringFrame[] frames = Enumerable.Range(1, 50)
                .Select(index => index * 20_000L)
                .Select(songTime =>
                {
                    long arrival = anchorTimestamp + checked((long)Math.Round(songTime / rate)) + latency;
                    UtaMappedGameplayTime mapped = mapper.MapCaptureCentre(arrival, 0, latency);
                    Assert.Multiple(() =>
                    {
                        Assert.That(mapped.SongTimeMicroseconds, Is.EqualTo(songTime).Within(1));
                        Assert.That(mapped.TimelineEpoch, Is.EqualTo(epoch));
                    });
                    return new UtaScoringFrame(
                        mapped.SongTimeMicroseconds,
                        (60 + transpose + (octaveFold ? 12 : 0)) * 100,
                        1000,
                        true,
                        mapped.TimelineEpoch);
                })
                .ToArray();

            var options = new UtaScoringOptions
            {
                TransposeSemitones = transpose,
                AllowOctaveTolerance = octaveFold,
                TimelineEpoch = epoch,
            };
            UtaPerformanceScore score = new UtaScoringEngine(options).Score(
                new[] { UtaScoringTarget.FromConfidence(0, 0, 1_000_000, 60, 1, UtaScoringNoteKind.Normal) },
                frames);

            Assert.That(score.Notes.Single().Grade, Is.EqualTo(UtaNoteGrade.Perfect),
                $"transpose={transpose} octave={octaveFold} rate={rate} latency={latency} epoch={epoch}");
        }
    }

    [Test]
    public async Task RecordingSoakRetriesPreserveFramesAndReleaseEachTake()
    {
        float[] samples = Enumerable.Repeat(0.25f, 480).ToArray();

        for (int retry = 0; retry < 20; retry++)
        {
            string path = Path.Combine(root, $"retry-{retry}.wav");
            await using var session = new UtaRecordingSession();
            session.Start(path, 48_000, 1, new UtaRecordingMetadata(), queueCapacityBlocks: 128);

            for (int block = 0; block < 80; block++)
                Assert.That(session.TryWrite(samples, 48_000, 1, block, 1), Is.True);

            UtaRecordingMetadata result = await session.StopAsync();
            Assert.Multiple(() =>
            {
                Assert.That(result.Complete, Is.True, $"retry={retry}");
                Assert.That(result.FrameCount, Is.EqualTo(80 * samples.Length), $"retry={retry}");
                Assert.That(File.Exists(path), Is.True, $"retry={retry}");
            });
        }

        Assert.That(Directory.GetFiles(root, "*.wav"), Has.Length.EqualTo(20));
    }

    [Test]
    public async Task RecordingDeviceFormatChangeAndDiskFailureAreReportedWithoutDataLossClaims()
    {
        float[] samples = Enumerable.Repeat(0.25f, 480).ToArray();

        await using (var formatChanged = new UtaRecordingSession())
        {
            formatChanged.StartDeferred(Path.Combine(root, "format-change.wav"), new UtaRecordingMetadata());
            Assert.That(formatChanged.TryWrite(samples, 48_000, 1, 1, 1), Is.True);
            Assert.That(formatChanged.TryWrite(samples, 44_100, 1, 2, 1), Is.False);

            UtaRecordingMetadata result = await formatChanged.StopAsync();
            Assert.Multiple(() =>
            {
                Assert.That(result.Complete, Is.False);
                Assert.That(result.FaultReason, Is.EqualTo(UtaRecordingFaultReason.FormatChanged));
            });
        }

        string directoryPath = Path.Combine(root, "not-a-wave-file");
        Directory.CreateDirectory(directoryPath);
        await using var diskFailure = new UtaRecordingSession();
        diskFailure.StartDeferred(directoryPath, new UtaRecordingMetadata());
        Assert.That(diskFailure.TryWrite(samples, 48_000, 1, 1, 1), Is.True);

        UtaRecordingMetadata failed = await diskFailure.StopAsync();
        Assert.Multiple(() =>
        {
            Assert.That(failed.Complete, Is.False);
            Assert.That(failed.FaultReason, Is.EqualTo(UtaRecordingFaultReason.DiskWriteFailed));
            Assert.That(failed.FrameCount, Is.Zero);
        });
    }

    [Test]
    public void TenMinuteStreamingSessionKeepsRealtimeBuffersBounded()
    {
        const int noteCount = 600;
        const long noteDuration = 900_000;
        const long noteSpacing = 1_000_000;
        UtaScoringTarget[] targets = Enumerable.Range(0, noteCount)
            .Select(index => UtaScoringTarget.FromConfidence(
                index,
                index * noteSpacing,
                index * noteSpacing + noteDuration,
                60,
                1,
                UtaScoringNoteKind.Normal))
            .ToArray();
        var session = new UtaStreamingScoringSession(targets);

        for (long time = 10_000; time < noteCount * noteSpacing; time += 20_000)
            session.AddFrame(new UtaScoringFrame(time, 6000, 1000, true));

        for (int index = 0; index < noteCount; index++)
        {
            long watermark = index * noteSpacing + noteDuration
                             + UtaScoringOptions.DEFAULT_COMMIT_DELAY_MICROSECONDS;
            session.AdvanceWatermark(watermark);
        }

        Assert.Multiple(() =>
        {
            Assert.That(session.CompletedNotes, Has.Count.EqualTo(noteCount));
            Assert.That(session.CompletedNotes.Values.All(note => note.Grade == UtaNoteGrade.Perfect), Is.True);
            Assert.That(session.MaximumRealtimeFrameWindow, Is.LessThan(80));
            Assert.That(session.RealtimeBufferedFrameCount, Is.Zero);
        });
    }

    [Test]
    public async Task RemoteServiceRepeatedStartStopReleasesPrivateListeners()
    {
        for (int attempt = 0; attempt < 10; attempt++)
        {
            using var credentials = new UtaRemoteCredentialStore();
            await using var server = new UtaRemoteServer(new AcceptAllRemoteTarget(), () => UtaRemoteSnapshot.Empty(), credentials);
            await server.StartAsync(reserveFreeTcpPort());
            Assert.That(server.IsRunning, Is.True);
            await server.StopAsync();
            Assert.That(server.IsRunning, Is.False);
        }
    }

    private static int reserveFreeTcpPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private sealed class AcceptAllRemoteTarget : IUtaRemoteCommandTarget
    {
        public ValueTask<UtaRemoteCommandResult> ExecuteAsync(UtaRemoteCommand command, CancellationToken cancellationToken)
            => ValueTask.FromResult(UtaRemoteCommandResult.Ok());
    }
}
