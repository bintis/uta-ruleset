// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using NUnit.Framework;
using osu.Game.Beatmaps;
using osu.Game.IO;
using osu.Game.Rulesets;
using osu.Game.Rulesets.Karaoke.Beatmaps;
using osu.Game.Rulesets.Karaoke.Beatmaps.Formats;
using osu.Game.Rulesets.Karaoke.Integration.Uta;
using osu.Game.Rulesets.Karaoke.Objects;
using osu.Game.Rulesets.Mods;
using osu.Game.Screens.Select;
using osu.Game.Utils;

namespace osu.Game.Rulesets.Karaoke.Tests.Integration.Uta;

[TestFixture]
public class UtzBeatmapSetConverterTest
{
    [Test]
    public void TestSongSelectOnlyIncludesNativeKaraokeMaps()
    {
        var filter = new KaraokeFilterCriteria();
        var globalCriteria = new FilterCriteria { AllowConvertedBeatmaps = true };

        Assert.Multiple(() =>
        {
            Assert.That(filter.Matches(new BeatmapInfo(new KaraokeRuleset().RulesetInfo), globalCriteria), Is.True);
            Assert.That(filter.Matches(new BeatmapInfo(new RulesetInfo { ShortName = "osu" }), globalCriteria), Is.False);
            Assert.That(filter.Matches(new BeatmapInfo(new RulesetInfo { ShortName = "mania" }), globalCriteria), Is.False);
        });
    }

    [Test]
    public void TestConverterToleratesTransientForeignSelection()
    {
        var regularBeatmap = new Beatmap();
        var karaokeBeatmap = new Beatmap
        {
            HitObjects = { new LegacyProperties { UtaPackageId = "uta:test" } },
        };

        Assert.Multiple(() =>
        {
            Assert.That(new KaraokeBeatmapConverter(regularBeatmap, new KaraokeRuleset()).CanConvert(), Is.True);
            Assert.That(new KaraokeBeatmapConverter(karaokeBeatmap, new KaraokeRuleset()).CanConvert(), Is.True);
        });
    }

    [Test]
    public void TestKaraokeModsAreVisibleToNativeSelector()
    {
        var ruleset = new KaraokeRuleset();
        Mod[] difficulty = ruleset.GetModsFor(ModType.DifficultyIncrease).SelectMany(ModUtils.FlattenMod).ToArray();
        Mod[] assistance = ruleset.GetModsFor(ModType.DifficultyReduction).SelectMany(ModUtils.FlattenMod).ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(difficulty.Select(mod => mod.Acronym), Is.EquivalentTo(new[] { "FL", "NPG", "NL" }));
            Assert.That(difficulty, Has.All.Property(nameof(Mod.HasImplementation)).True);
            Assert.That(assistance.Select(mod => mod.Acronym), Is.EquivalentTo(new[] { "VOX" }));
            Assert.That(assistance, Has.All.Property(nameof(Mod.HasImplementation)).True);
            Assert.That(ruleset.GetModsFor(ModType.Fun), Is.Empty);
        });
    }

    [Test]
    public void TestProducesPlayableArchiveWithAllMedia()
    {
        using var source = UtzPackageTest.CreatePackage();
        using var converted = new MemoryStream();
        UtzBeatmapSetConverter.Convert(source, converted);
        converted.Position = 0;

        using var archive = new ZipArchive(converted, ZipArchiveMode.Read, true);

        Assert.Multiple(() =>
        {
            Assert.That(archive.GetEntry(UtzBeatmapSetConverter.BEATMAP_FILENAME), Is.Not.Null);
            Assert.That(archive.GetEntry("audio/instrumental.mp3"), Is.Not.Null);
            Assert.That(archive.GetEntry("audio/original.mp3"), Is.Not.Null);
            Assert.That(archive.GetEntry("artwork/cover.jpg"), Is.Not.Null);
            Assert.That(archive.GetEntry("video/background.mp4"), Is.Not.Null);
            Assert.That(archive.GetEntry("manifest.json"), Is.Not.Null);
        });

        var beatmapEntry = archive.GetEntry(UtzBeatmapSetConverter.BEATMAP_FILENAME)!;
        using var reader = new LineBufferedReader(beatmapEntry.Open());
        Beatmap decoded = new KaraokeLegacyBeatmapDecoder().Decode(reader);
        var beatmap = (KaraokeBeatmap)new KaraokeBeatmapConverter(decoded, new KaraokeRuleset()).Convert();
        var note = beatmap.HitObjects.OfType<Note>().Single();

        Assert.Multiple(() =>
        {
            Assert.That(beatmap.UtaPackageId, Is.EqualTo("uta:test"));
            Assert.That(beatmap.UtaOriginalAudioFile, Is.EqualTo("audio/original.mp3"));
            Assert.That(beatmap.UtaTranscriptSegments, Has.Count.EqualTo(1));
            Assert.That(beatmap.HitObjects.OfType<Lyric>(), Is.Empty);
            Assert.That(beatmap.Scorable, Is.True);
            Assert.That(note.Midi, Is.EqualTo(69));
            Assert.That(note.StartTime, Is.EqualTo(0));
            Assert.That(note.Duration, Is.EqualTo(1000));
            Assert.That(decoded.BeatmapInfo.Metadata.AudioFile, Is.EqualTo("audio/instrumental.mp3"));
            Assert.That(decoded.BeatmapInfo.Metadata.BackgroundFile, Is.EqualTo("artwork/cover.jpg"));
            Assert.That(UtaKeySignature.FromMetadataTags(decoded.BeatmapInfo.Metadata.Tags), Is.EqualTo("G#m"));
        });

        var attributes = new KaraokeRuleset().GetBeatmapAttributesForDisplay(decoded.BeatmapInfo, Array.Empty<Mod>()).ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(attributes, Has.Length.EqualTo(1));
            Assert.That(attributes[0].Label.ToString(), Is.EqualTo("Key signature"));
            Assert.That(attributes[0].ValueFormat, Is.EqualTo("'G#m'"));
        });
    }
}
