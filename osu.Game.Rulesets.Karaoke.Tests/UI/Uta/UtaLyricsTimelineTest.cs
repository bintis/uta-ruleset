// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Generic;
using NUnit.Framework;
using osu.Game.Rulesets.Karaoke.Integration.Uta;
using osu.Game.Rulesets.Karaoke.UI.Uta;

namespace osu.Game.Rulesets.Karaoke.Tests.UI.Uta;

[TestFixture]
public class UtaLyricsTimelineTest
{
    private static readonly IReadOnlyList<UtaTranscriptSegment> segments = new[]
    {
        new UtaTranscriptSegment
        {
            Text = "First lyric",
            Start = 30,
            End = 32,
            Words = new[]
            {
                new UtaTranscriptWord { Word = "First", Start = 30, End = 31 },
                new UtaTranscriptWord { Word = "lyric", Start = 31, End = 32 },
            },
        },
        new UtaTranscriptSegment
        {
            Text = "Second lyric",
            Start = 35,
            End = 37,
            Words = new[]
            {
                new UtaTranscriptWord { Word = "Second", Start = 35, End = 36 },
                new UtaTranscriptWord { Word = "lyric", Start = 36, End = 37 },
            },
        },
    };

    [Test]
    public void TestUpcomingLinesRemainVisibleDuringLongIntro()
    {
        var frame = UtaLyricsTimeline.Evaluate(segments, 0);

        Assert.Multiple(() =>
        {
            Assert.That(frame.SegmentIndex, Is.Zero);
            Assert.That(frame.Visible, Is.True);
            Assert.That(frame.Countdown, Is.Null);
        });
    }

    [Test]
    public void TestShortGapKeepsFinishedLineUntilNextLeadIn()
    {
        Assert.Multiple(() =>
        {
            Assert.That(UtaLyricsTimeline.Evaluate(segments, 33).SegmentIndex, Is.Zero);
            Assert.That(UtaLyricsTimeline.Evaluate(segments, 33).Visible, Is.True);
            Assert.That(UtaLyricsTimeline.Evaluate(segments, 34.9).SegmentIndex, Is.EqualTo(1));
        });
    }

    [Test]
    public void TestFinalLineHidesAfterLinger()
    {
        Assert.That(UtaLyricsTimeline.Evaluate(segments, 40).Visible, Is.False);
    }

    [Test]
    public void TestWordHighlightInterpolatesFromLeadTime()
    {
        var word = segments[0].Words[0];

        Assert.Multiple(() =>
        {
            Assert.That(UtaLyricsTimeline.WordProgress(word, 29.75, true), Is.Zero);
            Assert.That(UtaLyricsTimeline.WordProgress(word, 30.25, true), Is.EqualTo(0.5).Within(0.000001));
            Assert.That(UtaLyricsTimeline.WordProgress(word, 30.75, true), Is.EqualTo(1));
        });
    }

    [Test]
    public void TestMissingWordTimingUsesUnicodeCharacters()
    {
        var normalized = UtaLyricsTimeline.Normalize(new[]
        {
            new UtaTranscriptSegment { Text = "歌🎤", Start = 1, End = 4 },
        });

        Assert.Multiple(() =>
        {
            Assert.That(normalized[0].Words, Has.Count.EqualTo(2));
            Assert.That(normalized[0].Words[1].Word, Is.EqualTo("🎤"));
            Assert.That(normalized[0].Words[0].Estimated, Is.True);
            Assert.That(normalized[0].Words[1].End, Is.EqualTo(4));
        });
    }
}
