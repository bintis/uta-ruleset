// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENSE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using osu.Game.Rulesets.Uta.Core;
using osu.Game.Rulesets.Uta.Formats;
using osu.Game.Rulesets.Uta.Mods;
using osu.Game.Rulesets.Uta.Performance;
using osu.Game.Rulesets.Uta.Scoring;
using osu.Game.Scoring;

namespace osu.Game.Rulesets.Uta.Tests;

[TestFixture]
public class UtaGameplayScoringIntegrationTests
{
    [Test]
    public void SignedMicrophoneLatencyMapsBeforeRateConversion()
    {
        var mapper = new UtaGameplayTimelineMapper(1_000_000);
        mapper.Reset(1_000_000, 0, 1);

        UtaMappedGameplayTime mapped = mapper.MapCaptureCentre(
            arrivalTimestamp: 1_000_000,
            analysisWindowDurationMicroseconds: 40_000,
            microphoneLatencyMicroseconds: -100_000);

        Assert.That(mapped.SongTimeMicroseconds, Is.EqualTo(80_000));
    }

    [Test]
    public void CaptureQueueAcceptsNegativeLatencyCalibration()
    {
        var mapper = new UtaGameplayTimelineMapper(1_000_000);
        mapper.Reset(1_000_000, 0, 1);
        var target = UtaScoringTarget.FromConfidence(0, 0, 200_000, 69, 1, UtaScoringNoteKind.Normal);
        var session = new UtaStreamingScoringSession(new[] { target });
        var queue = new UtaCaptureFrameQueue(4);
        queue.TryEnqueue(new UtaCapturedPitchFrame(440, 1000, -120, 1_000_000, 40_000));

        Assert.That(() => queue.DrainTo(mapper, -100_000, session), Throws.Nothing);
    }

    [Test]
    public void CaptureQueueCanMapRecordingReplayWithoutScoringSession()
    {
        var mapper = new UtaGameplayTimelineMapper(1_000_000);
        mapper.Reset(0, 0, 1);
        var queue = new UtaCaptureFrameQueue(4);
        queue.TryEnqueue(new UtaCapturedPitchFrame(440, 1000, -120, 100_000, 40_000));
        var mappedFrames = new List<UtaScoringFrame>();

        int drained = queue.DrainTo(
            mapper,
            0,
            session: null,
            mappedFrameConsumer: (_, mapped) => mappedFrames.Add(mapped));

        Assert.Multiple(() =>
        {
            Assert.That(drained, Is.EqualTo(1));
            Assert.That(mappedFrames, Has.Count.EqualTo(1));
            Assert.That(mappedFrames[0].PitchCents, Is.EqualTo(6900));
            Assert.That(queue.Count, Is.Zero);
        });
    }

    [Test]
    public void UtaNoteChoosesNativeScoringOrIgnoredJudgement()
    {
        var normal = new UtaNote
        {
            StartTime = 0,
            Duration = 1000,
            Midi = 69,
            TargetConfidence = 1,
            ScoringIndex = 0,
            NoteKind = "normal",
            ScoringEnabled = true,
        };
        var spoken = new UtaNote
        {
            StartTime = 0,
            Duration = 1000,
            Midi = null,
            TargetConfidence = 1,
            ScoringIndex = 1,
            NoteKind = "spoken",
            ScoringEnabled = true,
        };

        var beatmap = new UtaBeatmap();
        beatmap.HitObjects.Add(normal);
        beatmap.HitObjects.Add(spoken);

        // Scoring is on by default (no mod required); Relax is the opt-out.
        Assert.Multiple(() =>
        {
            Assert.That(normal.CreateJudgement(), Is.TypeOf<UtaJudgement>());
            Assert.That(spoken.CreateJudgement(), Is.TypeOf<UtaIgnoredJudgement>());
        });

        new UtaModRelax().ApplyToBeatmap(beatmap);

        Assert.Multiple(() =>
        {
            Assert.That(normal.CreateJudgement(), Is.TypeOf<UtaIgnoredJudgement>());
            Assert.That(spoken.CreateJudgement(), Is.TypeOf<UtaIgnoredJudgement>());
        });
    }

    [Test]
    public void NativeScoreProcessorRemainsZeroWithoutAppliedResults()
    {
        var note = new UtaNote
        {
            StartTime = 0,
            Duration = 1000,
            Midi = 69,
            TargetConfidence = 1,
            ScoringIndex = 0,
            NoteKind = "normal",
        };
        var beatmap = new UtaBeatmap();
        beatmap.HitObjects.Add(note);

        var processor = new UtaScoreProcessor(new UtaRuleset());
        processor.ApplyBeatmap(beatmap);
        var info = new ScoreInfo();
        processor.PopulateScore(info);

        Assert.Multiple(() =>
        {
            Assert.That(processor.TotalScore.Value, Is.Zero);
            Assert.That(processor.CompositeRating.Value, Is.Zero);
            Assert.That(processor.AccurateStreak.Value, Is.Zero);
            Assert.That(info.TotalScore, Is.Zero);
            Assert.That(info.Accuracy, Is.Zero);
        });
    }

    [Test]
    public void NativeScoreProcessorUsesContinuousUtaUnits()
    {
        var note = new UtaNote
        {
            StartTime = 0,
            Duration = 1000,
            Midi = 69,
            TargetConfidence = 1,
            ScoringIndex = 0,
            NoteKind = "normal",
            ScoringEnabled = true,
        };
        var beatmap = new UtaBeatmap();
        beatmap.HitObjects.Add(note);

        UtaNoteScore noteScore = new UtaScoringEngine().Score(
            new[] { UtaScoringBeatmapAdapter.CreateTarget(note) },
            Enumerable.Range(0, 51).Select(index => new UtaScoringFrame(index * 20_000, 6900, 1000, true))).Notes.Single();
        var result = new UtaJudgementResult(note, note.Judgement);
        result.Populate(noteScore, 0);

        var processor = new UtaScoreProcessor(new UtaRuleset());
        processor.ApplyBeatmap(beatmap);
        processor.ApplyResult(result);
        var info = new ScoreInfo();
        processor.PopulateScore(info);

        Assert.Multiple(() =>
        {
            Assert.That(processor.TotalScore.Value, Is.EqualTo(UtaScoreProcessor.DISPLAY_MAX_SCORE));
            Assert.That(processor.PitchAccuracy.Value, Is.EqualTo(1).Within(0.0001));
            Assert.That(processor.AccurateStreak.Value, Is.EqualTo(1));
            Assert.That(info.TotalScore, Is.EqualTo(UtaScoreProcessor.DISPLAY_MAX_SCORE));
            Assert.That(info.Accuracy, Is.EqualTo(1).Within(0.0001));
        });
    }

    [Test]
    public void NativeAndArchiveScoreUseTheSameIntegerRounding()
    {
        var note = new UtaNote
        {
            StartTime = 0,
            Duration = 1000,
            Midi = 69,
            TargetConfidence = 1,
            ScoringIndex = 0,
            NoteKind = "normal",
            ScoringEnabled = true,
        };
        var beatmap = new UtaBeatmap();
        beatmap.HitObjects.Add(note);
        UtaPerformanceScore performance = new UtaScoringEngine().Score(
            new[] { UtaScoringBeatmapAdapter.CreateTarget(note) },
            Enumerable.Range(0, 51).Select(index => new UtaScoringFrame(index * 20_000, 7000, 1000, true)));
        var result = new UtaJudgementResult(note, note.Judgement);
        result.Populate(performance.Notes.Single(), 0);

        var processor = new UtaScoreProcessor(new UtaRuleset());
        processor.ApplyBeatmap(beatmap);
        processor.ApplyResult(result);

        long expectedDisplayScore = UtaScoreProcessor.ToDisplayScore(performance.TotalScore);
        Assert.That(processor.TotalScore.Value, Is.EqualTo(expectedDisplayScore));
    }

    [Test]
    public void StreamingSessionRejectsFramesBehindCommittedWatermark()
    {
        var target = UtaScoringTarget.FromConfidence(0, 0, 200_000, 69, 1, UtaScoringNoteKind.Normal);
        var session = new UtaStreamingScoringSession(new[] { target });
        Assert.That(session.TryAddFrame(new UtaScoringFrame(20_000, 6900, 1000, true)), Is.True);

        session.AdvanceWatermark(100_000);

        Assert.Multiple(() =>
        {
            Assert.That(session.TryAddFrame(new UtaScoringFrame(80_000, 6900, 1000, true)), Is.False);
            Assert.That(session.RejectedLateFrames, Is.EqualTo(1));
        });
    }

    [Test]
    public void RelaxUsesExplicitNameAndHealthPolicy()
    {
        var mod = new UtaModRelax();

        Assert.Multiple(() =>
        {
            Assert.That(mod.Name, Is.EqualTo("Relax"));
            Assert.That(mod.Acronym, Is.EqualTo("RX"));
            Assert.That(mod.CreateHealthProcessor(0), Is.TypeOf<UtaPassiveHealthProcessor>());
        });
    }

    [Test]
    public void RulesetDefaultHealthProcessorScoresWithoutAnyMod()
    {
        Assert.That(new UtaRuleset().CreateHealthProcessor(0), Is.TypeOf<UtaScoringModeHealthProcessor>());
    }

    [Test]
    public void RecordingModIsAnExplicitOptIn()
    {
        var mod = new UtaModRecording();

        Assert.Multiple(() =>
        {
            Assert.That(mod.Name, Is.EqualTo("Recording"));
            Assert.That(mod.Acronym, Is.EqualTo("REC"));
            Assert.That(mod.IncompatibleMods, Does.Contain(typeof(UtaModAutoplay)));
        });
    }

    [Test]
    public void PhraseAggregationUsesCompletedNoteScores()
    {
        var target = UtaScoringTarget.FromConfidence(0, 0, 1_000_000, 69, 1, UtaScoringNoteKind.Normal);
        UtaNoteScore score = new UtaScoringEngine().Score(
            new[] { target },
            Enumerable.Range(0, 51).Select(index => new UtaScoringFrame(index * 20_000, 6900, 1000, true))).Notes.Single();
        var segment = new UtaTranscriptSegment { Text = "test", Start = 0, End = 1 };

        UtaPerformancePhraseSummary phrase = UtaPerformancePhraseAggregator.Aggregate(new[] { segment }, new[] { score }).Single();

        Assert.Multiple(() =>
        {
            Assert.That(phrase.Text, Is.EqualTo("test"));
            Assert.That(phrase.PitchAccuracyPermille, Is.EqualTo(1000));
            Assert.That(phrase.CoveragePermille, Is.EqualTo(1000));
            Assert.That(phrase.MissedIntervals, Is.Empty);
        });
    }
}
