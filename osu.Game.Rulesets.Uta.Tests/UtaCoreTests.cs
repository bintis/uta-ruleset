// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENSE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NUnit.Framework;
using osu.Game.Beatmaps;
using osu.Game.IO;
using osu.Game.Rulesets;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Uta;
using osu.Game.Rulesets.Uta.Configuration;
using osu.Game.Rulesets.Uta.Core;
using osu.Game.Rulesets.Uta.Formats;
using osu.Game.Rulesets.Uta.Mods;
using osu.Game.Rulesets.Uta.Pitch;
using osu.Game.Rulesets.Uta.UI;
using osu.Game.Rulesets.Uta.UI.HUD.Lyrics;
using osu.Game.Rulesets.Uta.UI.HUD.Pitch;
using osu.Game.Screens.Select;

namespace osu.Game.Rulesets.Uta.Tests;

[TestFixture]
public class UtaCoreTests
{
    [Test]
    public void SharedAudioStatePropagatesVolumeChanges()
    {
        var ruleset = new UtaRuleset();
        var config = new UtaRulesetConfigManager(null, ruleset.RulesetInfo);
        var state = new UtaAudioSettingsState();
        var meter = new osu.Framework.Bindables.BindableFloat();
        var audioConsumer = new osu.Framework.Bindables.BindableFloat();

        state.Initialise(config);
        meter.BindTo(state.OriginalVocalsVolume);
        audioConsumer.BindTo(state.OriginalVocalsVolume);
        meter.Value = 0.12f;

        Assert.That(audioConsumer.Value, Is.EqualTo(0.12f));
        state.Dispose();
    }

    [Test]
    public void AudioMathKeepsTransposeRateNeutralAndGatesVocals()
    {
        (double upFreq, double upTempo) = UtaAudioMath.TransposeFactors(12);
        (double downFreq, double downTempo) = UtaAudioMath.TransposeFactors(-12);

        Assert.Multiple(() =>
        {
            Assert.That(UtaAudioMath.TransposeFactors(0), Is.EqualTo((1d, 1d)));
            Assert.That(upFreq * upTempo, Is.EqualTo(1).Within(1e-9));
            Assert.That(upFreq, Is.EqualTo(2).Within(1e-9));
            Assert.That(downFreq * downTempo, Is.EqualTo(1).Within(1e-9));
            Assert.That(downFreq, Is.EqualTo(0.5).Within(1e-9));
            Assert.That(UtaAudioMath.EffectiveVocalsVolume(false, 0.8f), Is.EqualTo(0));
            Assert.That(UtaAudioMath.EffectiveVocalsVolume(true, 0.8f), Is.EqualTo(0.8f));
            Assert.That(UtaAudioMath.OriginalVocalsShouldPlay(false, false), Is.False);
            Assert.That(UtaAudioMath.OriginalVocalsShouldPlay(true, false), Is.True);
            Assert.That(UtaAudioMath.OriginalVocalsShouldPlay(false, true), Is.True, "切歌 with empty constructor mods must keep the last original-vocals preference.");
            Assert.That(UtaAudioMath.NeedsRoutedBgm(false, 0), Is.False);
            Assert.That(UtaAudioMath.NeedsRoutedBgm(true, 0), Is.True);
            Assert.That(UtaAudioMath.NeedsRoutedBgm(false, 1), Is.True);
            Assert.That(UtaAudioMath.NeedsRoutedVocals(false, false), Is.False);
            Assert.That(UtaAudioMath.NeedsRoutedVocals(true, false), Is.True);
            Assert.That(UtaAudioMath.NeedsRoutedVocals(false, true), Is.True, "VOX must follow routed BGM after leftover halt.");
            Assert.That(UtaAudioMath.DriftNeedsCorrection(1000, 1024), Is.False);
            Assert.That(UtaAudioMath.DriftNeedsCorrection(1000, 1030), Is.True);
        });
    }

    [Test]
    public void HaltedRoutedPlaybackIsANoOpWithoutLiveStreams()
        => Assert.That(UtaRoutedAudioStream.HaltAll(), Is.EqualTo(0));

    [Test]
    public void BassPlaceholderOutputsCannotHostAMixer()
    {
        Assert.Multiple(() =>
        {
            Assert.That(UtaAudioDevices.IsPlaceholderOutput(null), Is.True);
            Assert.That(UtaAudioDevices.IsPlaceholderOutput(""), Is.True);
            Assert.That(UtaAudioDevices.IsPlaceholderOutput("Default"), Is.True);
            Assert.That(UtaAudioDevices.IsPlaceholderOutput("No Sound"), Is.True);
            Assert.That(UtaAudioDevices.IsPlaceholderOutput("Default Audio Device"), Is.True);
            Assert.That(UtaAudioDevices.IsPlaceholderOutput("MARANTZ M4U: USB Audio"), Is.False);
            Assert.That(UtaAudioDevices.IsPlaceholderOutput("AKG C44-USB Microphone: USB Audio"), Is.False);
        });
    }

    [Test]
    public void PitchCurveUsesNightingaleHistoryMappingAndFixedSongViewport()
    {
        var ruleset = new UtaRuleset();
        var config = new UtaRulesetConfigManager(null, ruleset.RulesetInfo);
        var baseRange = new[]
        {
            new UtaNote { Midi = 48 },
            new UtaNote { Midi = 67 },
        };
        var highSong = new[]
        {
            new UtaNote { Midi = 72 },
            new UtaNote { Midi = 76 },
        };

        Assert.Multiple(() =>
        {
            Assert.That(UtaPitchCurveGraph.AgeAlpha(0, 200), Is.EqualTo(0.25f).Within(0.0001));
            Assert.That(UtaPitchCurveGraph.AgeAlpha(199, 200), Is.EqualTo(1).Within(0.0001));
            Assert.That(UtaPitchCurveGraph.TimeToX(8250, 10000, 400), Is.EqualTo(0).Within(0.0001));
            Assert.That(UtaPitchCurveGraph.TimeToX(10000, 10000, 400), Is.EqualTo(100).Within(0.0001));
            Assert.That(UtaPitchCurveGraph.TimeOffsetToX(10000, 10020, 400), Is.EqualTo(-8f / 7).Within(0.0001));
            Assert.That(UtaPitchCurveGraph.TimeToX(12000, 0, 400) + UtaPitchCurveGraph.TimeOffsetToX(0, 10000, 400),
                Is.EqualTo(UtaPitchCurveGraph.TimeToX(12000, 10000, 400)).Within(0.0001));
            Assert.That(UtaPitchCurveGraph.MidiToY(57.5f, 57.5f, 168), Is.EqualTo(84).Within(0.0001));
            Assert.That(UtaPitchGuideRenderer.CalculateFixedCentre(baseRange), Is.EqualTo(57.5f));
            Assert.That(UtaPitchGuideRenderer.CalculateFixedCentre(highSong), Is.EqualTo(68));
            Assert.That(config.GetBindable<UtaPitchCurveDisplay>(UtaRulesetSetting.PitchCurveDisplay).Value,
                Is.EqualTo(UtaPitchCurveDisplay.Both));
            Assert.That(config.GetBindable<bool>(UtaRulesetSetting.ShowPitchGuideTrail).Value, Is.False);
            Assert.That(config.GetBindable<bool>(UtaRulesetSetting.OriginalVocalsEnabled).Value, Is.False);
            Assert.That(config.GetBindable<float>(UtaRulesetSetting.PitchSamplingInterval).Value, Is.EqualTo(10));
        });
    }

    [Test]
    public void RulesetIdentityAndFilterAreUtaOnly()
    {
        var ruleset = new UtaRuleset();
        var filter = new UtaFilterCriteria();
        var criteria = new FilterCriteria { AllowConvertedBeatmaps = true };
        using var inputManager = new UtaInputManager(ruleset.RulesetInfo);
        Mod[] difficultyIncreaseMods = ruleset.GetModsFor(ModType.DifficultyIncrease).ToArray();
        Mod[] difficultyReductionMods = ruleset.GetModsFor(ModType.DifficultyReduction).ToArray();
        Mod[] funMods = ruleset.GetModsFor(ModType.Fun).ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(ruleset.ShortName, Is.EqualTo("uta"));
            Assert.That(ruleset.Description, Is.EqualTo("uta!"));
            Assert.That(inputManager.UseParentInput, Is.True);
            Assert.That(difficultyIncreaseMods,
                Has.Exactly(1).TypeOf<UtaModHideLyrics>()
                   .And.Exactly(1).TypeOf<UtaModHidePitchGuide>()
                   .And.Exactly(1).TypeOf<UtaModNightcore>());
            Assert.That(difficultyReductionMods, Has.Exactly(1).TypeOf<UtaModRelax>()
                                                       .And.Exactly(1).TypeOf<UtaModOriginalVocals>()
                                                       .And.Exactly(1).TypeOf<UtaModOctaveFold>().And.Exactly(1).TypeOf<UtaModDaycore>());
            Assert.That(funMods, Has.Exactly(1).TypeOf<UtaModAutoplay>().And.Exactly(1).TypeOf<UtaModRecording>());
            Assert.That(difficultyIncreaseMods
                .Concat(difficultyReductionMods)
                .Concat(funMods)
                .All(mod => mod.HasImplementation), Is.True);
            Assert.That(ruleset.GetBeatmapAttributesForDisplay(new BeatmapInfo(ruleset.RulesetInfo), Array.Empty<Mod>()), Is.Empty);
            Assert.That(filter.Matches(new BeatmapInfo(ruleset.RulesetInfo), criteria), Is.True);
            Assert.That(filter.Matches(new BeatmapInfo(new RulesetInfo { ShortName = "osu" }), criteria), Is.False);
        });
    }

    [Test]
    public void PackageRejectsPathTraversal()
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, true))
            archive.CreateEntry("../escape").Open().Dispose();

        stream.Position = 0;
        Assert.That(() => UtzPackage.Open(stream), Throws.TypeOf<InvalidDataException>());
    }

    [Test]
    public void PackageConvertsToNativePlayableBeatmap()
    {
        using var source = createLatestPackage();
        using var converted = new MemoryStream();
        UtzBeatmapSetConverter.Convert(source, converted);
        converted.Position = 0;

        using var archive = new ZipArchive(converted, ZipArchiveMode.Read, true);
        Assert.Multiple(() =>
        {
            Assert.That(archive.GetEntry("uta.osu"), Is.Not.Null);
            Assert.That(archive.GetEntry("audio/instrumental.mp3"), Is.Not.Null);
            Assert.That(archive.GetEntry("audio/guide-vocals.ogg"), Is.Not.Null);
            Assert.That(archive.GetEntry("audio/original.mp3"), Is.Not.Null);
            Assert.That(archive.GetEntry("artwork/cover.jpg"), Is.Not.Null);
            Assert.That(archive.GetEntry("video/background.mp4"), Is.Not.Null);
        });

        using var reader = new LineBufferedReader(archive.GetEntry("uta.osu")!.Open());
        Beatmap decoded = new UtaBeatmapDecoder().Decode(reader);
        var playable = (UtaBeatmap)new UtaBeatmapConverter(decoded, new UtaRuleset()).Convert();
        UtaNote note = playable.HitObjects.OfType<UtaNote>().OrderBy(item => item.StartTime).First();

        Assert.Multiple(() =>
        {
            Assert.That(playable.PackageId, Is.EqualTo("org.example.v3test"));
            Assert.That(playable.Transcript, Has.Count.EqualTo(1));
            Assert.That(playable.GuideVocalsFile, Is.EqualTo("audio/guide-vocals.ogg"));
            Assert.That(playable.OriginalAudioFile, Is.EqualTo("audio/original.mp3"));
            Assert.That(note.Midi, Is.EqualTo(69));
            Assert.That(note.StartTime, Is.Zero);
            Assert.That(note.Duration, Is.EqualTo(300));
            Assert.That(note, Is.Not.SameAs(decoded.HitObjects.OfType<UtaNote>().OrderBy(item => item.StartTime).First()),
                "Playable notes must be clones so a same-chart restart cannot mutate live drawable hitobjects.");
            Assert.That(decoded.BeatmapInfo.Metadata.AudioFile, Is.EqualTo("audio/instrumental.mp3"));
            Assert.That(decoded.BeatmapInfo.Metadata.BackgroundFile, Is.EqualTo("artwork/cover.jpg"));
        });
    }

    [Test]
    public void PackageConvertsCurrentVocalChartToNativePlayableBeatmap()
    {
        using var source = createLatestPackage();
        using var converted = new MemoryStream();
        UtzBeatmapSetConverter.Convert(source, converted);
        converted.Position = 0;

        using var archive = new ZipArchive(converted, ZipArchiveMode.Read, true);
        using var reader = new LineBufferedReader(archive.GetEntry("uta.osu")!.Open());
        Beatmap decoded = new UtaBeatmapDecoder().Decode(reader);
        var playable = (UtaBeatmap)new UtaBeatmapConverter(decoded, new UtaRuleset()).Convert();
        UtaNote[] notes = playable.HitObjects.OfType<UtaNote>().OrderBy(note => note.StartTime).ToArray();

        Assert.Multiple(() =>
        {
            // Duet parts 1 and 2 both use role "lead"; part 1 (three notes) must win over part 2 (one note).
            Assert.That(playable.PackageId, Is.EqualTo("org.example.v3test"));
            Assert.That(notes, Has.Length.EqualTo(3));

            Assert.That(notes[0].Midi, Is.EqualTo(69));
            Assert.That(notes[0].StartTime, Is.EqualTo(0));
            Assert.That(notes[0].Duration, Is.EqualTo(300));
            Assert.That(notes[0].NoteKind, Is.EqualTo("normal"));

            Assert.That(notes[1].Midi, Is.EqualTo(71));
            Assert.That(notes[1].StartTime, Is.EqualTo(300));
            Assert.That(notes[1].Duration, Is.EqualTo(200));

            // Rhythm-scored rap note carries no pitch target even though the chart is pitched-capable.
            Assert.That(notes[2].Midi, Is.Null);
            Assert.That(notes[2].StartTime, Is.EqualTo(600));
            Assert.That(notes[2].Duration, Is.EqualTo(400));
            Assert.That(notes[2].NoteKind, Is.EqualTo("golden_rap"));

            Assert.That(playable.Transcript, Has.Count.EqualTo(1));
            Assert.That(playable.Transcript[0].Text, Is.EqualTo("歌 姫"));
            Assert.That(playable.Transcript[0].Words, Has.Count.EqualTo(2));
            // The continuation token on note n2 must extend word w1's end into n2's span (melisma).
            Assert.That(playable.Transcript[0].Words[0].End, Is.EqualTo(0.5).Within(0.0001));
            Assert.That(playable.Transcript[0].Words[1].Start, Is.EqualTo(0.6).Within(0.0001));
        });
    }

    [Test]
    public void PackageAcceptsCurrentFormatVersion()
    {
        using var source = createLatestPackage();
        UtzPackage package = UtzPackage.Open(source);

        Assert.That(package.Manifest.FormatVersion, Is.EqualTo("0.3.0"));
    }

    [TestCase("0.1.0")]
    [TestCase("0.2.1")]
    [TestCase("0.4.0")]
    public void PackageRejectsEveryNonCurrentFormatVersion(string version)
    {
        using var source = createLatestPackage(formatVersion: version);
        Assert.That(() => UtzPackage.Open(source), Throws.TypeOf<InvalidDataException>());
    }

    [Test]
    public void PackageAcceptsANoteWithNoLyricTokensYet()
    {
        // A wordless note is a normal authoring-in-progress state, not a format violation.
        using var source = createLatestPackage(omitLyricsOnThirdNote: true);
        Assert.That(() => UtzPackage.Open(source), Throws.Nothing);
    }

    [Test]
    public void DetectsConcertA()
    {
        const int sample_rate = 48000;
        float[] signal = Enumerable.Range(0, 4096)
                                   .Select(index => (float)(0.2 * Math.Sin(2 * Math.PI * 440 * index / sample_rate)))
                                   .ToArray();

        Assert.That(UtaPitchDetector.Detect(signal, sample_rate), Is.EqualTo(440).Within(2));
        Assert.That(UtaPitchMath.FrequencyToMidi(440), Is.EqualTo(69).Within(0.001));
    }

    [TestCase(110)]
    [TestCase(220)]
    [TestCase(880)]
    public void DetectsRepresentativeVoicePitches(double hertz)
    {
        const int sample_rate = 48000;
        float[] signal = Enumerable.Range(0, 4096)
                                   .Select(index => (float)(0.2 * Math.Sin(2 * Math.PI * hertz * index / sample_rate)))
                                   .ToArray();

        Assert.That(UtaPitchDetector.Detect(signal, sample_rate), Is.EqualTo(hertz).Within(2));
    }

    [Test]
    public void PitchDetectorRejectsSilence()
    {
        Assert.That(UtaPitchDetector.Detect(new float[2048], 48000), Is.Null);
    }

    [Test]
    public void LyricsEstimateMissingWordTiming()
    {
        var segments = UtaLyricsTimeline.Normalize(new[]
        {
            new UtaTranscriptSegment { Text = "uta", Start = 1, End = 2 },
        });

        UtaLyricsFrame frame = UtaLyricsTimeline.Evaluate(segments, 1.5);
        var reusableProgress = new double[3];
        UtaLyricsFrame reusableFrame = UtaLyricsTimeline.Evaluate(segments, 1.5, 0, reusableProgress);
        Assert.Multiple(() =>
        {
            Assert.That(segments[0].Words, Has.Count.EqualTo(3));
            Assert.That(frame.Visible, Is.True);
            Assert.That(frame.WordProgress, Has.Count.EqualTo(3));
            Assert.That(reusableFrame.WordProgress, Is.SameAs(reusableProgress));
        });
    }

    [Test]
    public void LyricsPresentationPreservesLatencySignAndSeeksDirectly()
    {
        var state = new UtaLyricsPresentationState();
        state.SetSegments(new[]
        {
            new UtaTranscriptSegment { Text = "first", Start = 1, End = 2 },
            new UtaTranscriptSegment { Text = "second", Start = 5, End = 6 },
        });

        UtaLyricsPresentationUpdate delayed = state.Update(1000, 200);
        double delayedProgress = delayed.Frame.WordProgress[0];
        UtaLyricsPresentationUpdate onTime = state.Update(1000, 0);
        double onTimeProgress = onTime.Frame.WordProgress[0];
        UtaLyricsPresentationUpdate seek = state.Update(5200, 0);

        Assert.Multiple(() =>
        {
            Assert.That(delayedProgress, Is.LessThan(onTimeProgress), "Positive latency must delay, not advance, word progress.");
            Assert.That(seek.Frame.SegmentIndex, Is.EqualTo(1));
            Assert.That(seek.StructuralChange, Is.True);
        });
    }

    [Test]
    public void PitchViewportGlidesTowardTargetWithinRateLimit()
    {
        Assert.Multiple(() =>
        {
            // Within the snap tolerance, stays put rather than jittering by a fraction of a semitone.
            Assert.That(UtaPitchViewport.StepCentre(60f, 60.1f, 1f), Is.EqualTo(60f));
            // No elapsed time, no movement, however far the target is.
            Assert.That(UtaPitchViewport.StepCentre(50f, 70f, 0f), Is.EqualTo(50f));
            // Capped at the configured move rate (2.4 semitones/second) regardless of distance.
            Assert.That(UtaPitchViewport.StepCentre(50f, 70f, 1f), Is.EqualTo(52.4f).Within(0.001f));
            Assert.That(UtaPitchViewport.StepCentre(70f, 50f, 1f), Is.EqualTo(67.6f).Within(0.001f));
        });
    }

    [Test]
    public void FindPhrasesMergesActivityAcrossGapsBelowThreshold()
    {
        var segments = new[]
        {
            new UtaTranscriptSegment { Text = "a", Start = 0, End = 1 },
            new UtaTranscriptSegment { Text = "b", Start = 1.5, End = 2.5 },
            new UtaTranscriptSegment { Text = "c", Start = 10, End = 11 },
        };

        IReadOnlyList<UtaGapSkipController.Phrase> phrases = UtaGapSkipController.FindPhrases(segments, Array.Empty<UtaPitchNote>());

        Assert.Multiple(() =>
        {
            // The 500ms gap between segments a and b stays within one phrase; the 7500ms
            // gap before c exceeds the minimum and starts a new one.
            Assert.That(phrases, Has.Count.EqualTo(2));
            Assert.That(phrases[0].StartTime, Is.EqualTo(0));
            Assert.That(phrases[0].EndTime, Is.EqualTo(2500));
            Assert.That(phrases[1].StartTime, Is.EqualTo(10000));
            Assert.That(phrases[1].EndTime, Is.EqualTo(11000));
        });
    }

    [Test]
    public void PhraseIndexAtFindsMostRecentlyStartedPhrase()
    {
        var phrases = new[]
        {
            new UtaGapSkipController.Phrase(0, 1000),
            new UtaGapSkipController.Phrase(5000, 6000),
            new UtaGapSkipController.Phrase(10000, 11000),
        };

        Assert.Multiple(() =>
        {
            Assert.That(UtaPracticeController.PhraseIndexAt(phrases, 0), Is.EqualTo(0));
            Assert.That(UtaPracticeController.PhraseIndexAt(phrases, 4999), Is.EqualTo(0));
            Assert.That(UtaPracticeController.PhraseIndexAt(phrases, 5000), Is.EqualTo(1));
            Assert.That(UtaPracticeController.PhraseIndexAt(phrases, 9999), Is.EqualTo(1));
            Assert.That(UtaPracticeController.PhraseIndexAt(phrases, 20000), Is.EqualTo(2));
        });
    }

    [Test]
    public void MicLatencyScalesWithPlaybackRate()
    {
        Assert.Multiple(() =>
        {
            Assert.That(UtaInputManager.ComputePitchTime(10000, 100, 1), Is.EqualTo(9900));
            Assert.That(UtaInputManager.ComputePitchTime(10000, 100, 0.5), Is.EqualTo(9950));
            Assert.That(UtaInputManager.ComputePitchTime(10000, 100, 1.5), Is.EqualTo(9850));
            // A negative rate (rewind) still represents a positive real-time latency offset.
            Assert.That(UtaInputManager.ComputePitchTime(10000, 100, -1), Is.EqualTo(9900));
        });
    }

    [Test]
    public void PracticeModIsJustAGateForTheHud()
    {
        // UtaModPractice no longer touches rate itself. Live speed is a Tempo adjustment on
        // osu's TrackBass (UtaAudioSettingsState.PlaybackTempo). This mod only gates the HUD.
        var practice = new UtaModPractice();

        Assert.Multiple(() =>
        {
            Assert.That(practice.Name, Is.EqualTo("Practice"));
            Assert.That(practice.Acronym, Is.EqualTo("PR"));
            Assert.That(practice.Type, Is.EqualTo(ModType.Fun));
        });
    }

    private static MemoryStream createLatestPackage(string formatVersion = "0.3.0", bool omitLyricsOnThirdNote = false)
    {
        object textToken(string id, string text, string joinBefore, string? reading = null)
            => new { id, text, join_before = joinBefore, reading };
        object continuationToken(string continuationOf)
            => new { continuation_of = continuationOf };

        // Part 1 (lead) carries the notes that actually get played; part 2 exists only to prove
        // selectPlayableTrack() picks part 1 over another "lead"-role track when both are eligible.
        var vocalChart = new
        {
            format = "uta.vocal-chart",
            format_version = "1.1.0",
            timebase = 1_000_000,
            language = "ja",
            tracks = new object[]
            {
                new
                {
                    id = "lead",
                    role = "lead",
                    part = 1,
                    phrases = new object[]
                    {
                        new
                        {
                            id = "phrase-1",
                            notes = new object[]
                            {
                                new
                                {
                                    id = "n1",
                                    start = 0,
                                    duration = 300_000,
                                    pitch = new { midi = 69, cents = 0 },
                                    vocal_mode = "pitched",
                                    bonus = "normal",
                                    scoring = new { mode = "pitch", weight = 1 },
                                    lyrics = new[] { textToken("w1", "歌", "none") },
                                },
                                new
                                {
                                    id = "n2",
                                    start = 300_000,
                                    duration = 200_000,
                                    pitch = new { midi = 71, cents = 0 },
                                    vocal_mode = "pitched",
                                    bonus = "normal",
                                    scoring = new { mode = "pitch", weight = 1 },
                                    lyrics = new object[] { continuationToken("w1") },
                                },
                                new
                                {
                                    id = "n3",
                                    start = 600_000,
                                    duration = 400_000,
                                    pitch = (object?)null,
                                    vocal_mode = "rap",
                                    bonus = "golden",
                                    scoring = new { mode = "rhythm", weight = 1 },
                                    lyrics = omitLyricsOnThirdNote
                                        ? Array.Empty<object>()
                                        : new[] { textToken("w2", "姫", "space") },
                                },
                            },
                        },
                    },
                },
                new
                {
                    id = "lead-p2",
                    role = "lead",
                    part = 2,
                    singer = "Partner",
                    phrases = new object[]
                    {
                        new
                        {
                            id = "phrase-2-1",
                            notes = new object[]
                            {
                                new
                                {
                                    id = "n2-1",
                                    start = 0,
                                    duration = 1_000_000,
                                    pitch = new { midi = 60, cents = 0 },
                                    vocal_mode = "pitched",
                                    bonus = "normal",
                                    scoring = new { mode = "pitch", weight = 1 },
                                    lyrics = new[] { textToken("w2-1", "duet", "none") },
                                },
                            },
                        },
                    },
                },
            },
        };

        var files = new Dictionary<string, byte[]>
        {
            ["audio/instrumental.mp3"] = new byte[] { 10, 20, 30 },
            ["audio/guide-vocals.ogg"] = new byte[] { 11, 21, 31 },
            ["audio/original.mp3"] = new byte[] { 12, 22, 32 },
            ["charts/vocal.json"] = JsonSerializer.SerializeToUtf8Bytes(vocalChart),
            ["artwork/cover.jpg"] = new byte[] { 4, 5, 6 },
            ["video/background.mp4"] = new byte[] { 0, 1, 2, 3 },
        };

        object asset(string path, string mediaType)
        {
            byte[] bytes = files[path];
            return new
            {
                path,
                media_type = mediaType,
                sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
                bytes = bytes.Length,
            };
        }

        var manifest = new
        {
            format = "uta.song",
            format_version = formatVersion,
            package_id = "org.example.v3test",
            revision = 1,
            song = new { title = "Test", artist = "Uta", duration_seconds = 1.0 },
            audio = new
            {
                instrumental = asset("audio/instrumental.mp3", "audio/mpeg"),
                guide_vocals = asset("audio/guide-vocals.ogg", "audio/ogg"),
                original = asset("audio/original.mp3", "audio/mpeg"),
            },
            charts = new { vocal = asset("charts/vocal.json", "application/vnd.uta.vocal-chart+json;version=1") },
            visuals = new
            {
                cover = asset("artwork/cover.jpg", "image/jpeg"),
                video = asset("video/background.mp4", "video/mp4"),
            },
            required_features = new[] { "vocal-chart/1" },
        };

        var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, true))
        {
            add(archive, "manifest.json", JsonSerializer.SerializeToUtf8Bytes(manifest));
            foreach ((string path, byte[] bytes) in files)
                add(archive, path, bytes);
        }

        output.Position = 0;
        return output;
    }

    private static void add(ZipArchive archive, string path, byte[] bytes)
    {
        using Stream output = archive.CreateEntry(path).Open();
        output.Write(bytes);
    }
}
