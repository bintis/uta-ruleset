// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENSE file in the repository root for full licence text.

using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using osu.Game.Rulesets.Uta.Performance;
using osu.Game.Rulesets.Uta.Scoring;

namespace osu.Game.Rulesets.Uta.Tests;

[TestFixture]
public class UtaPerformanceArchiveTests
{
    private string root = null!;

    [SetUp]
    public void SetUp()
    {
        root = Path.Combine(Path.GetTempPath(), $"uta-performance-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(root))
            Directory.Delete(root, true);
    }

    [Test]
    public async Task ArchiveRoundTripAndLibraryLookup()
    {
        Guid performanceId = Guid.NewGuid();
        Guid scoreId = Guid.NewGuid();
        var manifest = new UtaPerformanceManifest
        {
            PerformanceId = performanceId,
            LazerScoreId = scoreId,
            Song = new UtaPerformanceSongInfo
            {
                PackageId = "uta:test",
                BeatmapHash = "abc",
                Title = "Test",
                Artist = "Uta",
            },
            Scoring = new UtaPerformanceScoringSummary { TotalScore = 900_000 },
            Recording = new UtaPerformanceRecordingInfo
            {
                SampleRate = 48_000,
                Channels = 1,
                CalibratedLatencyMilliseconds = 84,
                InputGain = 1.5,
            },
        };
        UtaPerformancePitchFrame[] frames =
        {
            new(10_000, 6900, 900, -230, true),
            new(30_000, 6910, 880, -220, true),
        };
        await using var recording = new MemoryStream(Encoding.UTF8.GetBytes("wave-data"));
        await using var waveform = new MemoryStream(new byte[] { 4, 5, 6 });
        var writer = new UtaPerformanceArchiveWriter(root);

        UtaPerformanceArchiveEntry written = await writer.WriteAsync(new UtaPerformanceWriteRequest(manifest, frames, recording, Waveform: waveform));
        var reader = new UtaPerformanceArchiveReader();
        UtaPerformanceManifest restored = await reader.ReadManifestAsync(written.DirectoryPath);
        var replay = await reader.ReadPitchReplayAsync(written.DirectoryPath);
        var library = new UtaPerformanceLibrary(root);
        await library.RefreshAsync();

        Assert.Multiple(() =>
        {
            Assert.That(File.Exists(Path.Combine(written.DirectoryPath, UtaPerformanceArchiveWriter.COMPLETE_FILENAME)), Is.True);
            Assert.That(File.Exists(Path.Combine(root, "index-v1.json")), Is.True);
            Assert.That(restored.PerformanceId, Is.EqualTo(performanceId));
            Assert.That(restored.LazerScoreId, Is.EqualTo(scoreId));
            Assert.That(restored.Files.Waveform, Is.EqualTo("waveform.bin"));
            Assert.That(restored.Recording?.SignalStage, Is.EqualTo("post_input_gain_pre_monitor"));
            Assert.That(replay, Is.EqualTo(frames));
            Assert.That(library.FindByLazerScoreId(scoreId)?.Manifest.PerformanceId, Is.EqualTo(performanceId));
        });
    }

    [Test]
    public async Task PracticeArchiveIsMarkedNonComparable()
    {
        var manifest = new UtaPerformanceManifest
        {
            PerformanceId = Guid.NewGuid(),
            Settings = new UtaPerformanceSettingsSnapshot { PracticeSession = true },
        };
        var writer = new UtaPerformanceArchiveWriter(root);

        UtaPerformanceArchiveEntry entry = await writer.WriteAsync(new UtaPerformanceWriteRequest(
            manifest,
            Array.Empty<UtaPerformancePitchFrame>()));
        UtaPerformanceManifest restored = await new UtaPerformanceArchiveReader().ReadManifestAsync(entry.DirectoryPath);

        Assert.Multiple(() =>
        {
            Assert.That(restored.Eligibility.Comparable, Is.False);
            Assert.That(restored.Eligibility.InvalidationReasons, Does.Contain(UtaPerformanceInvalidationReason.PracticeSession));
        });
    }

    [Test]
    public void RecordingWithoutMetadataIsRejected()
    {
        using var recording = new MemoryStream(new byte[] { 1, 2, 3 });
        var writer = new UtaPerformanceArchiveWriter(root);

        Assert.That(
            async () => await writer.WriteAsync(new UtaPerformanceWriteRequest(
                new UtaPerformanceManifest { PerformanceId = Guid.NewGuid() },
                Array.Empty<UtaPerformancePitchFrame>(),
                recording)),
            Throws.TypeOf<InvalidDataException>());
    }

    [Test]
    public void RecordingPathTraversalIsRejected()
    {
        var manifest = new UtaPerformanceManifest
        {
            PerformanceId = Guid.NewGuid(),
            Recording = new UtaPerformanceRecordingInfo(),
        };
        using var recording = new MemoryStream(new byte[] { 1, 2, 3 });
        var writer = new UtaPerformanceArchiveWriter(root);

        Assert.That(
            async () => await writer.WriteAsync(new UtaPerformanceWriteRequest(manifest, Array.Empty<UtaPerformancePitchFrame>(), recording, "../take.wav")),
            Throws.TypeOf<ArgumentException>());
    }

    [TestCase("../take.wav")]
    [TestCase("..\\take.wav")]
    [TestCase("sub/take.wav")]
    [TestCase("sub\\take.wav")]
    [TestCase("C:take.wav")]
    public void PortableAssetPathTraversalIsRejected(string fileName)
    {
        Assert.That(() => UtaPerformancePaths.ValidateFileName(fileName), Throws.TypeOf<ArgumentException>());
    }

    [Test]
    public async Task NativeScoreIdentityCanBeLinkedAfterArchiveCommit()
    {
        var writer = new UtaPerformanceArchiveWriter(root);
        UtaPerformanceArchiveEntry entry = await writer.WriteAsync(new UtaPerformanceWriteRequest(
            new UtaPerformanceManifest { PerformanceId = Guid.NewGuid() },
            Array.Empty<UtaPerformancePitchFrame>()));
        Guid scoreId = Guid.NewGuid();

        var linker = new UtaPerformanceScoreLinker();
        await linker.LinkAsync(entry.DirectoryPath, scoreId, "score-hash");
        var reader = new UtaPerformanceArchiveReader();
        UtaPerformanceManifest linked = await reader.ReadManifestAsync(entry.DirectoryPath);
        var library = new UtaPerformanceLibrary(root);
        await library.RefreshAsync();

        Assert.Multiple(() =>
        {
            Assert.That(linked.LazerScoreId, Is.EqualTo(scoreId));
            Assert.That(linked.LazerScoreHash, Is.EqualTo("score-hash"));
            Assert.That(library.FindByLazerScoreId(scoreId)?.Manifest.PerformanceId, Is.EqualTo(entry.Manifest.PerformanceId));
        });
    }

    [Test]
    public async Task TamperedAssetFailsChecksumVerification()
    {
        var manifest = new UtaPerformanceManifest { PerformanceId = Guid.NewGuid() };
        var writer = new UtaPerformanceArchiveWriter(root);
        UtaPerformanceArchiveEntry entry = await writer.WriteAsync(new UtaPerformanceWriteRequest(
            manifest,
            new[] { new UtaPerformancePitchFrame(10_000, 6900, 1000, -200, true) }));
        await File.AppendAllTextAsync(Path.Combine(entry.DirectoryPath, UtaPerformanceArchiveWriter.PITCH_REPLAY_FILENAME), "tamper");

        var reader = new UtaPerformanceArchiveReader();
        Assert.That(async () => await reader.ReadManifestAsync(entry.DirectoryPath), Throws.TypeOf<InvalidDataException>());
    }

    [Test]
    public void ExpressionAnalysisIsReportOnlyAndDetectsCompressionAndClipping()
    {
        UtaExpressionFrame[] frames = Enumerable.Range(0, 100)
                                                .Select(index => new UtaExpressionFrame(index * 20_000, -200, index < 5 ? (ushort)1000 : (ushort)500, true))
                                                .ToArray();

        UtaExpressionAnalysis result = UtaExpressionAnalyzer.Analyse(frames);

        Assert.Multiple(() =>
        {
            Assert.That(result.Available, Is.True);
            Assert.That(result.PossibleAutomaticGainControl, Is.True);
            Assert.That(result.ClippingRatioPermille, Is.EqualTo(50));
        });
    }
}
