// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENSE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using osu.Game.Rulesets.Uta.Scoring;

namespace osu.Game.Rulesets.Uta.Tests;

[TestFixture]
public class UtaHotPathRegressionTests
{
    [Test]
    public void DirectSingleNotePathMatchesFullPerformancePath()
    {
        UtaScoringTarget target = UtaScoringTarget.FromConfidence(
            17, 0, 1_000_000, 69, 0.91, UtaScoringNoteKind.Normal);
        UtaScoringFrame[] frames = Enumerable.Range(0, 50)
            .Select(index => new UtaScoringFrame(
                10_000 + index * 20_000L,
                6900 + (index % 5 - 2) * 8,
                (ushort)(850 + index % 100),
                true))
            .Reverse()
            .ToArray();
        var engine = new UtaScoringEngine();

        UtaNoteScore direct = engine.ScoreNote(target, (IEnumerable<UtaScoringFrame>)frames);
        UtaScoringFrame[] ordered = frames.OrderBy(frame => frame.TimeMicroseconds).ToArray();
        UtaNoteScore realtime = engine.ScoreNote(target, ordered.AsSpan());
        UtaNoteScore fromPerformance = engine.Score(new[] { target }, frames).Notes.Single();

        Assert.Multiple(() =>
        {
            Assert.That(direct, Is.EqualTo(fromPerformance));
            Assert.That(realtime, Is.EqualTo(fromPerformance));
        });
    }

    [Test]
    public void ResamplerKeepsExistingDuplicateFramePreference()
    {
        var options = new UtaScoringOptions();
        var resampler = new UtaPitchFrameResampler((IEnumerable<UtaScoringFrame>)new[]
        {
            new UtaScoringFrame(100_000, 6800, 1000, false),
            new UtaScoringFrame(100_000, 6900, 700, true),
            new UtaScoringFrame(100_000, 7000, 900, true),
            new UtaScoringFrame(120_000, 7100, 900, true, TimelineEpoch: 1),
        }, options);

        UtaResampledPitch sample = resampler.SampleAt(100_000);

        Assert.Multiple(() =>
        {
            Assert.That(sample.Voiced, Is.True);
            Assert.That(sample.PitchCents, Is.EqualTo(7000));
            Assert.That(sample.ClarityPermille, Is.EqualTo(900));
        });
    }

    [Test]
    public void WatermarkCommonPathReturnsSharedEmptyResult()
    {
        UtaScoringTarget target = UtaScoringTarget.FromConfidence(
            0, 0, 1_000_000, 69, 1, UtaScoringNoteKind.Normal);
        var session = new UtaStreamingScoringSession(new[] { target });

        IReadOnlyList<UtaNoteScore> first = session.AdvanceWatermark(100_000);
        IReadOnlyList<UtaNoteScore> second = session.AdvanceWatermark(200_000);

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.SameAs(Array.Empty<UtaNoteScore>()));
            Assert.That(second, Is.SameAs(Array.Empty<UtaNoteScore>()));
        });
    }

    [Test]
    public void StreamingResetPreservesFreshSessionBehaviour()
    {
        UtaScoringTarget target = UtaScoringTarget.FromConfidence(
            0, 0, 1_000_000, 69, 1, UtaScoringNoteKind.Normal);
        var session = new UtaStreamingScoringSession(new[] { target });
        for (long time = 10_000; time < 1_000_000; time += 20_000)
            session.AddFrame(new UtaScoringFrame(time, 6900, 1000, true));

        Assert.That(session.AdvanceWatermark(1_060_000), Has.Count.EqualTo(1));

        session.Reset(new UtaScoringOptions { TimelineEpoch = 1 });
        for (long time = 10_000; time < 1_000_000; time += 20_000)
            session.AddFrame(new UtaScoringFrame(time, 6900, 1000, true, TimelineEpoch: 1));

        IReadOnlyList<UtaNoteScore> completed = session.AdvanceWatermark(1_060_000);
        Assert.Multiple(() =>
        {
            Assert.That(completed, Has.Count.EqualTo(1));
            Assert.That(completed[0].Grade, Is.EqualTo(UtaNoteGrade.Perfect));
            Assert.That(session.RejectedLateFrames, Is.Zero);
            Assert.That(session.CompletedNotes, Has.Count.EqualTo(1));
        });
    }
}
