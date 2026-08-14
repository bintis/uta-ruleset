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
            Assert.That(UtaPitchCurveGraph.MidiToY(57.5f, 57.5f, 168), Is.EqualTo(84).Within(0.0001));
            Assert.That(UtaPitchGuide.CalculateFixedCentre(baseRange), Is.EqualTo(57.5f));
            Assert.That(UtaPitchGuide.CalculateFixedCentre(highSong), Is.EqualTo(68));
            Assert.That(config.GetBindable<UtaPitchCurveDisplay>(UtaRulesetSetting.PitchCurveDisplay).Value,
                Is.EqualTo(UtaPitchCurveDisplay.Both));
            Assert.That(config.GetBindable<bool>(UtaRulesetSetting.ShowPitchGuideTrail).Value, Is.False);
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

        Assert.Multiple(() =>
        {
            Assert.That(ruleset.ShortName, Is.EqualTo("uta"));
            Assert.That(ruleset.Description, Is.EqualTo("uta!"));
            Assert.That(inputManager.UseParentInput, Is.True);
            Assert.That(ruleset.GetModsFor(ModType.Fun), Is.Empty);
            Assert.That(difficultyIncreaseMods,
                Has.Exactly(1).TypeOf<UtaModHideLyrics>().And.Exactly(1).TypeOf<UtaModHidePitchGuide>());
            Assert.That(difficultyReductionMods, Has.Exactly(1).TypeOf<UtaModOriginalVocals>());
            Assert.That(difficultyIncreaseMods.Concat(difficultyReductionMods).All(mod => mod.HasImplementation), Is.True);
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

    [Test]
    public void LyricsEstimateMissingWordTiming()
    {
        var segments = UtaLyricsTimeline.Normalize(new[]
        {
            new UtaTranscriptSegment { Text = "uta", Start = 1, End = 2 },
        });

        UtaLyricsFrame frame = UtaLyricsTimeline.Evaluate(segments, 1.5);
        Assert.Multiple(() =>
        {
            Assert.That(segments[0].Words, Has.Count.EqualTo(3));
            Assert.That(frame.Visible, Is.True);
            Assert.That(frame.WordProgress, Has.Count.EqualTo(3));
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

    private static void add(ZipArchive archive, string path, byte[] bytes)
    {
        using Stream output = archive.CreateEntry(path).Open();
        output.Write(bytes);
    }
}
