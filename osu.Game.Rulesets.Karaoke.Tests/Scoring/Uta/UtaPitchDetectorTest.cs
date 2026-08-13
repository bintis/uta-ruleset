// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using NUnit.Framework;
using osu.Game.Rulesets.Karaoke.Scoring.Uta;

namespace osu.Game.Rulesets.Karaoke.Tests.Scoring.Uta;

[TestFixture]
public class UtaPitchDetectorTest
{
    [Test]
    public void TestDetectsCleanA4()
    {
        const double sampleRate = 48000;
        var samples = new float[2048];

        for (int i = 0; i < samples.Length; i++)
            samples[i] = (float)(Math.Sin(2 * Math.PI * 440 * i / sampleRate) * 0.2);

        Assert.That(UtaPitchDetector.Detect(samples, sampleRate), Is.EqualTo(440).Within(3));
    }

    [Test]
    public void TestSilenceIsUnvoiced()
    {
        Assert.That(UtaPitchDetector.Detect(new float[2048], 48000), Is.Null);
    }

    [Test]
    public void TestSimilarityBandsAndOctaveTolerance()
    {
        Assert.Multiple(() =>
        {
            Assert.That(UtaPitchMath.Similarity(440, 440, false), Is.EqualTo(1));
            Assert.That(UtaPitchMath.Similarity(440, 466.163761, false), Is.InRange(0.5, 0.9));
            Assert.That(UtaPitchMath.Similarity(440, 880, false), Is.EqualTo(0));
            Assert.That(UtaPitchMath.Similarity(440, 880, true), Is.EqualTo(1).Within(0.000000001));
        });
    }
}
