// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENSE file in the repository root for full licence text.

using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using osu.Game.Rulesets.Uta.Recording;
using osu.Game.Rulesets.Uta.Scoring;

namespace osu.Game.Rulesets.Uta.Tests;

[TestFixture]
public class UtaRecordingAndFollowUpTests
{
    private string root = null!;

    [SetUp]
    public void SetUp()
    {
        root = Path.Combine(Path.GetTempPath(), $"uta-recording-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(root))
            Directory.Delete(root, true);
    }

    [Test]
    public void BoundedPcmQueueRejectsWithoutBlocking()
    {
        var queue = new UtaPcmCaptureQueue(1);
        float[] block = new float[480];

        Assert.That(queue.TryWrite(block, 48_000, 1, 10, 1), Is.True);
        Assert.That(queue.TryWrite(block, 48_000, 1, 20, 1), Is.False);
        Assert.That(queue.RejectedBlocks, Is.EqualTo(1));
    }

    [Test]
    public void Pcm16WavWritesCanonicalHeader()
    {
        string path = Path.Combine(root, "take.wav");

        using (var writer = new UtaWavPcm16Writer(path, 48_000, 1))
        {
            writer.Write(new[] { -1f, -0.5f, 0f, 0.5f, 1f, 2f });
            writer.Finalise();
            Assert.That(writer.FramesWritten, Is.EqualTo(6));
            Assert.That(writer.ClippedSamples, Is.EqualTo(1));
        }

        byte[] bytes = File.ReadAllBytes(path);
        Assert.Multiple(() =>
        {
            Assert.That(System.Text.Encoding.ASCII.GetString(bytes, 0, 4), Is.EqualTo("RIFF"));
            Assert.That(System.Text.Encoding.ASCII.GetString(bytes, 8, 4), Is.EqualTo("WAVE"));
            Assert.That(System.Text.Encoding.ASCII.GetString(bytes, 36, 4), Is.EqualTo("data"));
            Assert.That(bytes.Length, Is.EqualTo(44 + 6 * 2));
        });
    }

    [Test]
    public async Task DeferredRecordingSessionFinalisesWithoutDroppingAcceptedFrames()
    {
        string path = Path.Combine(root, "deferred.wav");
        var session = new UtaRecordingSession();
        session.StartDeferred(path, new UtaRecordingMetadata());

        float[] samples = Enumerable.Repeat(0.25f, 480).ToArray();
        for (int i = 0; i < 20; i++)
            Assert.That(session.TryWrite(samples, 48_000, 1, i + 1, 1), Is.True);

        UtaRecordingMetadata result = await session.StopAsync();

        Assert.Multiple(() =>
        {
            Assert.That(result.Complete, Is.True);
            Assert.That(result.FrameCount, Is.EqualTo(480 * 20));
            Assert.That(result.SampleRate, Is.EqualTo(48_000));
            Assert.That(result.Channels, Is.EqualTo(1));
            Assert.That(File.Exists(path), Is.True);
        });
    }

    [Test]
    public void VocalRangeAdvisorFindsLowerTransposeForHighSong()
    {
        var advisor = new UtaVocalRangeAdvisor { MinimumObservationCount = 10 };
        for (int i = 0; i < 20; i++)
            advisor.AddObservation(5_700 + i * 10, 900);

        UtaScoringTarget[] targets =
        {
            UtaScoringTarget.FromConfidence(0, 0, 1_000_000, 64, 1, UtaScoringNoteKind.Normal),
            UtaScoringTarget.FromConfidence(1, 1_000_000, 2_000_000, 67, 1, UtaScoringNoteKind.Normal),
        };

        UtaTransposeRecommendation result = advisor.Recommend(targets);
        Assert.That(result.Available, Is.True);
        Assert.That(result.Semitones, Is.LessThan(0));
    }

    [TestCase(0.5)]
    [TestCase(1.0)]
    [TestCase(1.5)]
    public void TimelineMappingKeepsLatencyInRealMilliseconds(double rate)
    {
        const long frequency = 1_000_000;
        var mapper = new UtaGameplayTimelineMapper(frequency);
        mapper.Reset(1_000_000, 10_000_000, rate);

        UtaMappedGameplayTime mapped = mapper.MapCaptureCentre(
            arrivalTimestamp: 1_200_000,
            analysisWindowDurationMicroseconds: 40_000,
            microphoneLatencyMicroseconds: 80_000);

        // capture centre = 1.2 s - 0.02 s - 0.08 s = 1.1 s,
        // i.e. 100 ms real time after the anchor.
        long expected = 10_000_000 + checked((long)Math.Round(100_000 * rate));
        Assert.That(mapped.SongTimeMicroseconds, Is.EqualTo(expected));
    }

    [TestCase(-6, false, 0)]
    [TestCase(0, false, 0)]
    [TestCase(6, false, 0)]
    [TestCase(0, true, 12)]
    public void ScoringCombinationMatrixKeepsPerfectPitchDeterministic(int transpose, bool octaveFold, int octaveOffset)
    {
        var options = new UtaScoringOptions
        {
            TransposeSemitones = transpose,
            AllowOctaveTolerance = octaveFold,
        };
        var target = UtaScoringTarget.FromConfidence(
            0, 0, 1_000_000, 60, 1, UtaScoringNoteKind.Normal);

        UtaScoringFrame[] frames = Enumerable.Range(0, 51)
            .Select(i => new UtaScoringFrame(
                i * 20_000,
                (60 + transpose + octaveOffset) * 100,
                1000,
                true))
            .ToArray();

        UtaPerformanceScore score = new UtaScoringEngine(options).Score(new[] { target }, frames);
        Assert.That(score.Notes.Single().Grade, Is.EqualTo(UtaNoteGrade.Perfect));
    }
}
