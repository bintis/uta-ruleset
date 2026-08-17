// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENSE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using osu.Game.Rulesets.Scoring;
using osu.Game.Rulesets.Uta.Core;
using osu.Game.Rulesets.Uta.Scoring;

namespace osu.Game.Rulesets.Uta.Tests;

[TestFixture]
public class UtaScoringV2Tests
{
    [Test]
    public void ExactPitchProducesPerfectMillionScore()
    {
        UtaPerformanceScore score = scoreSingleNote(framesForCents(6900));

        Assert.Multiple(() =>
        {
            Assert.That(score.TotalScore, Is.EqualTo(1_000_000));
            Assert.That(score.Notes[0].Grade, Is.EqualTo(UtaNoteGrade.Perfect));
            Assert.That(score.Notes[0].NativeResult, Is.EqualTo(HitResult.Perfect));
            Assert.That(score.PitchAccuracyPermille, Is.EqualTo(1000));
            Assert.That(score.HighestCombo, Is.EqualTo(1));
            Assert.That(score.HighestAccurateStreak, Is.EqualTo(1));
        });
    }

    [TestCase(6965, UtaNoteGrade.Great, HitResult.Great)]
    [TestCase(6995, UtaNoteGrade.Good, HitResult.Good)]
    [TestCase(7000, UtaNoteGrade.Bad, HitResult.Meh)]
    public void QualityBandsMapToNativeResults(int sungPitchCents, UtaNoteGrade grade, HitResult native)
    {
        UtaNoteScore noteScore = scoreSingleNote(framesForCents(sungPitchCents)).Notes[0];

        Assert.Multiple(() =>
        {
            Assert.That(noteScore.Grade, Is.EqualTo(grade));
            Assert.That(noteScore.NativeResult, Is.EqualTo(native));
        });
    }

    [TestCase(7000, UtaPitchFault.High, UtaAnalysisMessage.LowerPitch)]
    [TestCase(6800, UtaPitchFault.Low, UtaAnalysisMessage.RaisePitch)]
    public void BadGradeKeepsDirectionalDiagnosis(int sungPitchCents, UtaPitchFault fault, UtaAnalysisMessage advice)
    {
        UtaPerformanceScore score = scoreSingleNote(framesForCents(sungPitchCents));

        Assert.Multiple(() =>
        {
            Assert.That(score.Notes[0].Grade, Is.EqualTo(UtaNoteGrade.Bad));
            Assert.That(score.Notes[0].Faults.HasFlag(fault), Is.True);
            Assert.That(score.Analysis.Advice, Is.EqualTo(advice));
        });
    }

    [Test]
    public void AlternatingHighAndLowIsBadAndUnstable()
    {
        var frames = new List<UtaScoringFrame>();
        for (long time = 10_000; time < 1_000_000; time += 20_000)
            frames.Add(new UtaScoringFrame(time, ((time / 20_000) & 1) == 0 ? 7000 : 6800, 1000, true));

        UtaNoteScore score = scoreSingleNote(frames).Notes[0];

        Assert.Multiple(() =>
        {
            Assert.That(score.Grade, Is.EqualTo(UtaNoteGrade.Bad));
            Assert.That(score.BiasCents, Is.Zero);
            Assert.That(score.Faults.HasFlag(UtaPitchFault.Unstable), Is.True);
        });
    }

    [Test]
    public void SilenceProducesMiss()
    {
        UtaPerformanceScore score = scoreSingleNote(unvoicedFrames());

        Assert.Multiple(() =>
        {
            Assert.That(score.TotalScore, Is.Zero);
            Assert.That(score.Notes[0].Grade, Is.EqualTo(UtaNoteGrade.Miss));
            Assert.That(score.Notes[0].NativeResult, Is.EqualTo(HitResult.Miss));
            Assert.That(score.Notes[0].Faults.HasFlag(UtaPitchFault.LowCoverage), Is.True);
        });
    }

    [Test]
    public void BadContinuesNativeComboButBreaksAccurateStreak()
    {
        UtaScoringTarget[] targets =
        {
            note(0, 1000, 69, 0),
            note(1000, 2000, 69, 1),
            note(2000, 3000, 69, 2),
            note(3000, 4000, 69, 3),
        };
        IEnumerable<UtaScoringFrame> frames = framesForCents(7000, 0, 1000)
            .Concat(framesForCents(7000, 1000, 2000))
            .Concat(unvoicedFrames(2000, 3000))
            .Concat(framesForCents(6900, 3000, 4000));

        UtaPerformanceScore score = new UtaScoringEngine().Score(targets, frames);

        Assert.Multiple(() =>
        {
            Assert.That(score.HighestCombo, Is.EqualTo(2));
            Assert.That(score.HighestAccurateStreak, Is.EqualTo(1));
            Assert.That(score.GradeCounts[UtaNoteGrade.Bad], Is.EqualTo(2));
        });
    }

    [Test]
    public void PerfectGreatGoodFormAccurateStreak()
    {
        UtaScoringTarget[] targets =
        {
            note(0, 1000, 69, 0),
            note(1000, 2000, 69, 1),
            note(2000, 3000, 69, 2),
            note(3000, 4000, 69, 3),
        };
        IEnumerable<UtaScoringFrame> frames = framesForCents(6900, 0, 1000)
            .Concat(framesForCents(6965, 1000, 2000))
            .Concat(framesForCents(6995, 2000, 3000))
            .Concat(framesForCents(7000, 3000, 4000));

        UtaPerformanceScore score = new UtaScoringEngine().Score(targets, frames);

        Assert.Multiple(() =>
        {
            Assert.That(score.HighestCombo, Is.EqualTo(4));
            Assert.That(score.HighestAccurateStreak, Is.EqualTo(3));
        });
    }

    [Test]
    public void PitchGateStopsStableOffPitchTechniqueBonus()
    {
        UtaPerformanceScore score = scoreSingleNote(framesForCents(7000));

        Assert.Multiple(() =>
        {
            Assert.That(score.Notes[0].LongToneQualityPermille, Is.GreaterThan(900));
            Assert.That(score.FinalProfile, Is.EqualTo(UtaScoringProfile.Faithful));
            Assert.That(score.TotalScore, Is.LessThan(750_000));
        });
    }


    [Test]
    public void TechniqueProfileFallsBackToFaithfulOnShortNotes()
    {
        UtaScoringTarget[] targets =
        {
            note(0, 200, 69, 0),
            note(200, 400, 69, 1),
            note(400, 1400, 69, 2),
        };
        IEnumerable<UtaScoringFrame> frames = framesForCents(6900, 0, 1400);

        UtaPerformanceScore score = new UtaScoringEngine().Score(targets, frames);

        Assert.Multiple(() =>
        {
            Assert.That(score.TotalScore, Is.EqualTo(1_000_000));
            Assert.That(score.Notes[0].Profiles.TechniquePermille, Is.EqualTo(score.Notes[0].Profiles.FaithfulPermille));
            Assert.That(score.Notes[1].Profiles.TechniquePermille, Is.EqualTo(score.Notes[1].Profiles.FaithfulPermille));
        });
    }

    [Test]
    public void ControlledVibratoIsDetectedAndNotTreatedAsRandomJitter()
    {
        var frames = new List<UtaScoringFrame>();
        for (long time = 10_000; time < 1_200_000; time += 20_000)
        {
            double seconds = time / 1_000_000.0;
            int cents = 6900 + (int)Math.Round(50 * Math.Sin(2 * Math.PI * 5 * seconds));
            frames.Add(new UtaScoringFrame(time, cents, 1000, true));
        }

        UtaNoteScore noteScore = new UtaScoringEngine().Score(
            new[] { UtaScoringTarget.FromConfidence(0, 0, 1_200_000, 69, 1, UtaScoringNoteKind.Normal) },
            frames).Notes[0];

        Assert.Multiple(() =>
        {
            Assert.That(noteScore.Vibrato.Detected, Is.True);
            Assert.That(noteScore.Vibrato.RateHertz, Is.EqualTo(5).Within(0.8));
            Assert.That(noteScore.StabilityPermille, Is.GreaterThan(noteScore.RawStabilityPermille));
        });
    }

    [Test]
    public void OctaveToleranceAndTransposeAreApplied()
    {
        var target = new[] { note(0, 1000, 69, 0) };
        UtaPerformanceScore strict = new UtaScoringEngine().Score(target, framesForCents(8100));
        UtaPerformanceScore folded = new UtaScoringEngine(new UtaScoringOptions { AllowOctaveTolerance = true }).Score(target, framesForCents(8100));
        UtaPerformanceScore transposed = new UtaScoringEngine(new UtaScoringOptions { TransposeSemitones = 2 }).Score(target, framesForCents(7100));

        Assert.Multiple(() =>
        {
            Assert.That(strict.PitchAccuracyPermille, Is.Zero);
            Assert.That(folded.TotalScore, Is.EqualTo(1_000_000));
            Assert.That(transposed.TotalScore, Is.EqualTo(1_000_000));
        });
    }

    [Test]
    public void TargetConfidenceWeightsSong()
    {
        UtaScoringTarget[] targets =
        {
            UtaScoringTarget.FromConfidence(0, 0, 1_000_000, 69, 1, UtaScoringNoteKind.Normal),
            UtaScoringTarget.FromConfidence(1, 1_000_000, 2_000_000, 69, 0.5, UtaScoringNoteKind.Normal),
        };
        IEnumerable<UtaScoringFrame> frames = framesForCents(6900, 0, 1000).Concat(unvoicedFrames(1000, 2000));

        UtaPerformanceScore score = new UtaScoringEngine().Score(targets, frames);

        Assert.That(score.PitchAccuracyPermille, Is.EqualTo(667).Within(1));
    }

    [Test]
    public void NonPitchKindsAndNullMidiAreIgnored()
    {
        UtaScoringTarget[] targets =
        {
            UtaScoringTarget.FromConfidence(0, 0, 1_000_000, null, 1, UtaScoringNoteKind.Spoken),
            UtaScoringTarget.FromConfidence(1, 1_000_000, 2_000_000, 69, 1, UtaScoringNoteKind.GoldenFreestyle),
            UtaScoringTarget.FromConfidence(2, 2_000_000, 3_000_000, 69, 0.49, UtaScoringNoteKind.Normal),
        };

        UtaPerformanceScore score = new UtaScoringEngine().Score(targets, Array.Empty<UtaScoringFrame>());

        Assert.Multiple(() =>
        {
            Assert.That(score.Notes.All(noteScore => noteScore.Grade == UtaNoteGrade.Ignored), Is.True);
            Assert.That(score.TotalScore, Is.Zero);
        });
    }

    [Test]
    public void FrameOrderAndSamplingDensityDoNotChangeConstantPitch()
    {
        UtaScoringTarget[] target = { note(0, 1000, 69, 0) };
        List<UtaScoringFrame> sparse = Enumerable.Range(0, 25)
                                                        .Select(index => new UtaScoringFrame(5_000 + index * 40_000L, 6900, 1000, true))
                                                        .ToList();
        var engine = new UtaScoringEngine();

        Assert.Multiple(() =>
        {
            Assert.That(engine.Score(target, sparse).TotalScore, Is.EqualTo(1_000_000));
            Assert.That(engine.Score(target, sparse.AsEnumerable().Reverse()).TotalScore, Is.EqualTo(1_000_000));
            Assert.That(engine.Score(target, framesForCents(6900)).TotalScore, Is.EqualTo(1_000_000));
        });
    }

    [Test]
    public void TimelineEpochSeparatesPracticeAttempts()
    {
        var target = new[] { note(0, 1000, 69, 0) };
        IEnumerable<UtaScoringFrame> frames = framesForCents(7000, timelineEpoch: 0)
            .Concat(framesForCents(6900, timelineEpoch: 1));

        UtaPerformanceScore first = new UtaScoringEngine(new UtaScoringOptions { TimelineEpoch = 0 }).Score(target, frames);
        UtaPerformanceScore second = new UtaScoringEngine(new UtaScoringOptions { TimelineEpoch = 1 }).Score(target, frames);

        Assert.Multiple(() =>
        {
            Assert.That(first.Notes[0].Grade, Is.EqualTo(UtaNoteGrade.Bad));
            Assert.That(second.Notes[0].Grade, Is.EqualTo(UtaNoteGrade.Perfect));
        });
    }

    [Test]
    public void StreamingSessionCommitsAfterWatermarkDelay()
    {
        var session = new UtaStreamingScoringSession(new[] { note(0, 1000, 69, 0) });
        foreach (UtaScoringFrame frame in framesForCents(6900))
            session.AddFrame(frame);

        Assert.Multiple(() =>
        {
            Assert.That(session.AdvanceWatermark(1_050_000), Is.Empty);
            Assert.That(session.AdvanceWatermark(1_060_000), Has.Count.EqualTo(1));
            Assert.That(session.CompletePerformance().TotalScore, Is.EqualTo(1_000_000));
        });
    }

    [Test]
    public void StreamingSessionIgnoresWatermarkRegressionWithoutThrowing()
    {
        var session = new UtaStreamingScoringSession(new[] { note(0, 1000, 69, 0) });
        foreach (UtaScoringFrame frame in framesForCents(6900))
            session.AddFrame(frame);

        Assert.Multiple(() =>
        {
            Assert.That(session.AdvanceWatermark(1_060_000), Has.Count.EqualTo(1));
            Assert.That(() => session.AdvanceWatermark(1_000_000), Throws.Nothing);
            Assert.That(session.AdvanceWatermark(1_080_000), Is.Empty);
            Assert.That(session.CompletedNotes, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public void StreamingSessionScoresBoundedPerNoteWindows()
    {
        const int noteCount = 300;
        const long noteDuration = 200_000;
        const long noteSpacing = 300_000;
        UtaScoringTarget[] targets = Enumerable.Range(0, noteCount)
            .Select(index => UtaScoringTarget.FromConfidence(
                index,
                index * noteSpacing,
                index * noteSpacing + noteDuration,
                69,
                1,
                UtaScoringNoteKind.Normal))
            .ToArray();
        var session = new UtaStreamingScoringSession(targets);

        // Preload a long performance to ensure the regression guard catches
        // whole-history rescoring rather than relying on update timing.
        for (long time = 10_000; time < noteCount * noteSpacing; time += 20_000)
            session.AddFrame(new UtaScoringFrame(time, 6900, 1000, true));

        for (int index = 0; index < noteCount; index++)
        {
            long watermark = index * noteSpacing + noteDuration
                             + UtaScoringOptions.DEFAULT_COMMIT_DELAY_MICROSECONDS;
            session.AdvanceWatermark(watermark);
        }

        Assert.Multiple(() =>
        {
            Assert.That(session.CompletedNotes, Has.Count.EqualTo(noteCount));
            Assert.That(session.CompletedNotes.Values.All(score => score.Grade == UtaNoteGrade.Perfect), Is.True);
            Assert.That(session.MaximumRealtimeFrameWindow, Is.LessThan(30));
            Assert.That(session.RealtimeBufferedFrameCount, Is.Zero);
            Assert.That(session.CompletePerformance().TotalScore, Is.EqualTo(1_000_000));
        });
    }

    [Test]
    public void StreamingSessionIgnoresNegativeLeadInFrames()
    {
        var session = new UtaStreamingScoringSession(new[] { note(0, 1000, 69, 0) });
        session.AddFrame(new UtaScoringFrame(-20_000, 7000, 1000, true));
        foreach (UtaScoringFrame frame in framesForCents(6900))
            session.AddFrame(frame);

        Assert.That(session.CompletePerformance().TotalScore, Is.EqualTo(1_000_000));
    }

    [Test]
    public void PartialGridBinsSampleInsideTheTargetInterval()
    {
        UtaScoringTarget target = UtaScoringTarget.FromConfidence(0, 19_000, 40_000, 69, 1, UtaScoringNoteKind.Normal);
        UtaScoringFrame[] frames =
        {
            new(10_000, 7000, 1000, true),
            new(19_500, 6900, 1000, true),
            new(30_000, 6900, 1000, true),
        };

        UtaNoteScore score = new UtaScoringEngine().ScoreNote(target, frames);

        Assert.That(score.Grade, Is.EqualTo(UtaNoteGrade.Perfect));
    }

    [Test]
    public void TimelineMapperPreservesHistoricalSegmentsAcrossSeek()
    {
        var mapper = new UtaGameplayTimelineMapper(1_000_000);
        mapper.Reset(0, 0, 1);
        UtaMappedGameplayTime capture = mapper.MapCaptureCentre(200_000, 40_000, 80_000);
        mapper.AddAnchor(1_000_000, 1_000_000, 0.5);
        UtaMappedGameplayTime beforeSeek = mapper.MapTimestamp(1_400_000);
        mapper.AddAnchor(1_500_000, 500_000, 1, true);
        UtaMappedGameplayTime historical = mapper.MapTimestamp(1_400_000);
        UtaMappedGameplayTime afterSeek = mapper.MapTimestamp(1_600_000);

        Assert.Multiple(() =>
        {
            Assert.That(capture, Is.EqualTo(new UtaMappedGameplayTime(100_000, 0)));
            Assert.That(beforeSeek, Is.EqualTo(new UtaMappedGameplayTime(1_200_000, 0)));
            Assert.That(historical, Is.EqualTo(beforeSeek));
            Assert.That(afterSeek, Is.EqualTo(new UtaMappedGameplayTime(600_000, 1)));
        });
    }

    [Test]
    public void BoundedFrameQueueReportsOverflowInsteadOfSilentlyDropping()
    {
        var queue = new UtaCaptureFrameQueue(2);
        var session = new UtaStreamingScoringSession(new[] { note(0, 1000, 69, 0) });

        Assert.Multiple(() =>
        {
            Assert.That(queue.TryEnqueue(capturedA4(100_000)), Is.True);
            Assert.That(queue.TryEnqueue(capturedA4(120_000)), Is.True);
            Assert.That(queue.TryEnqueue(capturedA4(140_000)), Is.False);
            Assert.That(queue.Overflowed, Is.True);
            Assert.That(queue.RejectedFrames, Is.EqualTo(1));
            var mapper = new UtaGameplayTimelineMapper(1_000_000);
            mapper.Reset(0, 0, 1);
            var mapped = new List<UtaScoringFrame>();
            Assert.That(queue.DrainTo(mapper, 0, session, (_, frame) => mapped.Add(frame)), Is.EqualTo(2));
            Assert.That(mapped.Select(frame => frame.TimeMicroseconds), Is.EqualTo(new[] { 80_000L, 100_000L }));
            Assert.That(queue.Count, Is.Zero);
        });
    }

    [Test]
    public void InvalidFloatingPointInputsAreRejectedAtTheBoundary()
    {
        Assert.Multiple(() =>
        {
            Assert.That(
                () => UtaScoringTarget.FromConfidence(0, 0, 1_000_000, 69, double.NaN, UtaScoringNoteKind.Normal),
                Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(
                () => UtaScoringFrame.FromHertz(double.PositiveInfinity, 440, 1),
                Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(
                () => UtaScoringFrame.FromHertz(0, 440, double.NaN),
                Throws.TypeOf<ArgumentOutOfRangeException>());
        });
    }

    [Test]
    public void BeatmapAdapterPreservesCurrentKindsAndMetadata()
    {
        var beatmap = new UtaBeatmap();
        beatmap.HitObjects.Add(new UtaNote
        {
            StartTime = 125,
            Duration = 875,
            Midi = null,
            NoteKind = "golden_spoken",
            TargetConfidence = 0.8,
            ScoringIndex = 4,
        });

        UtaScoringTarget target = UtaScoringBeatmapAdapter.CreateTargets(beatmap).Single();

        Assert.Multiple(() =>
        {
            Assert.That(target.Index, Is.EqualTo(4));
            Assert.That(target.Midi, Is.Null);
            Assert.That(target.Kind, Is.EqualTo(UtaScoringNoteKind.GoldenSpoken));
            Assert.That(target.ConfidencePermille, Is.EqualTo(800));
        });
    }

    private static UtaPerformanceScore scoreSingleNote(IEnumerable<UtaScoringFrame> frames)
        => new UtaScoringEngine().Score(new[] { note(0, 1000, 69, 0) }, frames);

    private static UtaScoringTarget note(double startMilliseconds, double endMilliseconds, int? midi, int index)
        => UtaScoringTarget.FromConfidence(
            index,
            (long)Math.Round(startMilliseconds * 1000),
            (long)Math.Round(endMilliseconds * 1000),
            midi,
            1,
            UtaScoringNoteKind.Normal);

    private static UtaCapturedPitchFrame capturedA4(long arrivalTimestamp)
        => UtaCapturedPitchFrame.FromAnalysis(440, 1, 0.1, arrivalTimestamp, 40);

    private static IEnumerable<UtaScoringFrame> framesForCents(
        int pitchCents,
        double startMilliseconds = 0,
        double endMilliseconds = 1000,
        int timelineEpoch = 0)
    {
        long start = (long)Math.Round(startMilliseconds * 1000) + 10_000;
        long end = (long)Math.Round(endMilliseconds * 1000);
        for (long time = start; time < end; time += 20_000)
            yield return new UtaScoringFrame(time, pitchCents, 1000, true, timelineEpoch);
    }

    private static IEnumerable<UtaScoringFrame> unvoicedFrames(double startMilliseconds = 0, double endMilliseconds = 1000)
    {
        long start = (long)Math.Round(startMilliseconds * 1000) + 10_000;
        long end = (long)Math.Round(endMilliseconds * 1000);
        for (long time = start; time < end; time += 20_000)
            yield return new UtaScoringFrame(time, 0, 1000, false);
    }
}
