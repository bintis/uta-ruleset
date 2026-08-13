// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENCE file in the repository root for full licence text.

using NUnit.Framework;
using osu.Game.Rulesets.Karaoke.Scoring.Uta;
using osu.Game.Rulesets.Scoring;
using osu.Game.Scoring;

namespace osu.Game.Rulesets.Karaoke.Tests.Scoring.Uta;

[TestFixture]
public class UtaPitchScoringTest
{
    [Test]
    public void TestPerfectSustainedNoteIsOsuS()
    {
        var scoring = new UtaPitchScoring();

        for (int step = 1; step <= 20; step++)
        {
            scoring.AddFrame(
                step * 0.05,
                440,
                440,
                1,
                0,
                0,
                69,
                false);
        }

        var summary = scoring.GetSummary();
        var note = scoring.GetNoteScores()[0];

        Assert.Multiple(() =>
        {
            Assert.That(summary.Accuracy, Is.EqualTo(1).Within(0.000000001));
            Assert.That(summary.Rank, Is.EqualTo(ScoreRank.S));
            Assert.That(note.Grade, Is.EqualTo(UtaNoteGrade.Perfect));
            Assert.That(note.HitResult, Is.EqualTo(HitResult.Perfect));
        });
    }

    [Test]
    public void TestMissingVoiceIsOsuDAndMiss()
    {
        var scoring = new UtaPitchScoring();

        for (int step = 1; step <= 20; step++)
            scoring.AddFrame(step * 0.05, 440, null, 0, 0, 0, 69, false);

        Assert.Multiple(() =>
        {
            Assert.That(scoring.GetSummary().Rank, Is.EqualTo(ScoreRank.D));
            Assert.That(scoring.GetNoteScores()[0].HitResult, Is.EqualTo(HitResult.Miss));
        });
    }

    [Test]
    public void TestResetClearsAccumulatedScore()
    {
        var scoring = new UtaPitchScoring();
        scoring.AddFrame(0.05, 440, 440, 1, 0, 0, 69, false);
        scoring.Reset();

        Assert.Multiple(() =>
        {
            Assert.That(scoring.GetSummary().Accuracy, Is.Zero);
            Assert.That(scoring.GetSummary().Rank, Is.EqualTo(ScoreRank.D));
            Assert.That(scoring.GetNoteScores(), Is.Empty);
        });
    }
}
