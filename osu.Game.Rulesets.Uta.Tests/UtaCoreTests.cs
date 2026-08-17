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
            Assert.That(UtaPitchGuide.CalculateFixedCentre(baseRange), Is.EqualTo(57.5f));
            Assert.That(UtaPitchGuide.CalculateFixedCentre(highSong), Is.EqualTo(68));
            Assert.That(config.GetBindable<UtaPitchCurveDisplay>(UtaRulesetSetting.PitchCurveDisplay).Value,
                Is.EqualTo(UtaPitchCurveDisplay.Both));
            Assert.That(config.GetBindable<bool>(UtaRulesetSetting.ShowPitchGuideTrail).Value, Is.False);
            Assert.That(config.GetBindable<float>(UtaRulesetSetting.PitchSamplingInterval).Value, Is.EqualTo(10));
        });
    }

    [Test]
    public void NoteColoursUseNightingaleWholeNoteGrades()
    {
        UtaNoteColourState perfect = noteColour(1, 0);
        UtaNoteColourState good = noteColour(0.8f, 0.5f);
        UtaNoteColourState high = noteColour(0.2f, 1.2f);
        UtaNoteColourState low = noteColour(0.2f, -1.2f);
        var miss = new UtaNoteColourState();
        miss.Accumulate(0.1, false, 0, 0);

        Assert.Multiple(() =>
        {
            Assert.That(perfect.Grade(), Is.EqualTo(UtaNoteColourGrade.Perfect));
            Assert.That(good.Grade(), Is.EqualTo(UtaNoteColourGrade.Good));
            Assert.That(high.Grade(), Is.EqualTo(UtaNoteColourGrade.High));
            Assert.That(low.Grade(), Is.EqualTo(UtaNoteColourGrade.Low));
            Assert.That(miss.Grade(), Is.EqualTo(UtaNoteColourGrade.Miss));
        });

        static UtaNoteColourState noteColour(float similarity, float deviation)
        {
            var state = new UtaNoteColourState();
            state.Accumulate(0.1, true, similarity, deviation);
            return state;
        }
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
        using var source = createPackage();
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
        UtaNote note = playable.HitObjects.OfType<UtaNote>().Single();

        Assert.Multiple(() =>
        {
            Assert.That(playable.PackageId, Is.EqualTo("uta:test"));
            Assert.That(playable.Transcript, Has.Count.EqualTo(1));
            Assert.That(playable.GuideVocalsFile, Is.EqualTo("audio/guide-vocals.ogg"));
            Assert.That(playable.OriginalAudioFile, Is.EqualTo("audio/original.mp3"));
            Assert.That(note.Midi, Is.EqualTo(69));
            Assert.That(note.StartTime, Is.Zero);
            Assert.That(note.Duration, Is.EqualTo(1000));
            Assert.That(decoded.BeatmapInfo.Metadata.AudioFile, Is.EqualTo("audio/instrumental.mp3"));
            Assert.That(decoded.BeatmapInfo.Metadata.BackgroundFile, Is.EqualTo("artwork/cover.jpg"));
        });
    }

    [Test]
    public void PackageConvertsUtz02VocalChartToNativePlayableBeatmap()
    {
        using var source = createV2Package();
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
            Assert.That(playable.PackageId, Is.EqualTo("org.example.v2test"));
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
    public void PackageAcceptsUtz03FormatVersion()
    {
        // 0.3 only adds optional, defaulted fields to the 0.2 manifest shape, so a 0.2 reader
        // must accept and route it the same way.
        using var source = createV2Package(formatVersion: "0.3.0");
        UtzPackage package = UtzPackage.Open(source);

        Assert.That(package.Manifest.IsFormatV2, Is.True);
    }

    [Test]
    public void PackageAcceptsANoteWithNoLyricTokensYet()
    {
        // A wordless note is a normal authoring-in-progress state, not a format violation.
        using var source = createV2Package(omitLyricsOnThirdNote: true);
        Assert.That(() => UtzPackage.Open(source), Throws.Nothing);
    }

    [Test]
    public void PackageWritesBreakForSkippableVocalGap()
    {
        using var source = createPackage(includeGap: true);
        using var converted = new MemoryStream();
        UtzBeatmapSetConverter.Convert(source, converted);
        converted.Position = 0;

        using var archive = new ZipArchive(converted, ZipArchiveMode.Read, true);
        using var reader = new StreamReader(archive.GetEntry("uta.osu")!.Open());
        Assert.That(reader.ReadToEnd(), Does.Contain("2,1000,5000"));
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
        // UtaModPractice no longer touches rate itself - IApplicableToRate is only evaluated once
        // at Player start, not continuously, so it can't drive a live mid-song speed slider. The
        // HUD instead binds straight to MasterGameplayClockContainer.UserPlaybackRate; this mod's
        // only remaining job is gating whether the practice HUD (P) exists at all.
        var practice = new UtaModPractice();

        Assert.Multiple(() =>
        {
            Assert.That(practice.Name, Is.EqualTo("Practice"));
            Assert.That(practice.Acronym, Is.EqualTo("PR"));
            Assert.That(practice.Type, Is.EqualTo(ModType.Fun));
        });
    }

    private static MemoryStream createPackage(bool includeGap = false)
    {
        string pitchNotes = includeGap
            ? "{\"format_version\":1,\"notes\":[{\"start\":0.0,\"end\":1.0,\"midi\":69,\"confidence\":1.0,\"kind\":\"golden\"},{\"start\":5.0,\"end\":6.0,\"midi\":69,\"confidence\":1.0,\"kind\":\"normal\"}]}"
            : "{\"format_version\":1,\"notes\":[{\"start\":0.0,\"end\":1.0,\"midi\":69,\"confidence\":1.0,\"kind\":\"golden\"}]}";
        var files = new Dictionary<string, byte[]>
        {
            ["audio/instrumental.mp3"] = new byte[] { 10, 20, 30 },
            ["audio/guide-vocals.ogg"] = new byte[] { 11, 21, 31 },
            ["audio/original.mp3"] = new byte[] { 12, 22, 32 },
            ["charts/transcript.json"] = Encoding.UTF8.GetBytes("{\"language\":\"ja\",\"segments\":[{\"text\":\"test\",\"start\":0.0,\"end\":1.0,\"words\":[]}] }"),
            ["charts/pitch-track.json"] = Encoding.UTF8.GetBytes("{\"format_version\":1,\"model\":null,\"hop_seconds\":0.01,\"frames\":[]}"),
            ["charts/pitch-notes.json"] = Encoding.UTF8.GetBytes(pitchNotes),
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
            format_version = "0.1.0",
            package_id = "uta:test",
            revision = 1,
            song = new { title = "Test", artist = "Uta", duration_seconds = 1.0 },
            audio = new
            {
                instrumental = asset("audio/instrumental.mp3", "audio/mpeg"),
                guide_vocals = asset("audio/guide-vocals.ogg", "audio/ogg"),
                original = asset("audio/original.mp3", "audio/mpeg"),
                audio_offset_seconds = 0,
            },
            charts = new
            {
                transcript = asset("charts/transcript.json", "application/json"),
                pitch_track = asset("charts/pitch-track.json", "application/json"),
                pitch_notes = asset("charts/pitch-notes.json", "application/json"),
            },
            visuals = new
            {
                cover = asset("artwork/cover.jpg", "image/jpeg"),
                video = asset("video/background.mp4", "video/mp4"),
            },
            scoring = new { engine = "uta.pitch", version = 1, octave_tolerance = false },
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

    private static MemoryStream createV2Package(string formatVersion = "0.2.1", bool omitLyricsOnThirdNote = false)
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
            ["audio/instrumental.ogg"] = new byte[] { 10, 20, 30 },
            ["charts/vocal.json"] = JsonSerializer.SerializeToUtf8Bytes(vocalChart),
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
            package_id = "org.example.v2test",
            revision = 1,
            song = new { title = "Test", artist = "Uta", duration_seconds = 1.0 },
            audio = new { instrumental = asset("audio/instrumental.ogg", "audio/ogg") },
            charts = new { vocal = asset("charts/vocal.json", "application/vnd.uta.vocal-chart+json;version=1") },
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
