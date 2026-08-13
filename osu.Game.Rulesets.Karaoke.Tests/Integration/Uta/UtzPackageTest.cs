// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using NUnit.Framework;
using osu.Game.Rulesets.Karaoke.Integration.Uta;
using osu.Game.Rulesets.Karaoke.UI.Uta;

namespace osu.Game.Rulesets.Karaoke.Tests.Integration.Uta;

[TestFixture]
public class UtzPackageTest
{
    [Test]
    public void TestFindsOnlyLongMiddleGapsForNativeSkip()
    {
        var segments = new[]
        {
            new UtaTranscriptSegment { Start = 0, End = 2, Text = "first" },
            new UtaTranscriptSegment { Start = 10, End = 12, Text = "second" },
        };
        var notes = new[]
        {
            new UtaPitchNote { Start = 1, End = 3, Midi = 60 },
            new UtaPitchNote { Start = 9, End = 11, Midi = 62 },
        };

        var gaps = UtaGapSkipController.FindSkippableGaps(segments, notes);

        Assert.That(gaps, Is.EqualTo(new[] { new UtaGapSkipController.SkippableGap(3000, 9000) }));
    }

    [Test]
    public void TestReadsPackageAndPreservesVideo()
    {
        using var packageStream = CreatePackage();
        var package = UtzPackage.Open(packageStream);

        Assert.Multiple(() =>
        {
            Assert.That(package.Manifest.Song.Title, Is.EqualTo("God knows..."));
            Assert.That(package.Transcript.Segments, Has.Count.EqualTo(1));
            Assert.That(package.PitchNotes, Has.Count.EqualTo(1));
            Assert.That(package.PitchNotes[0].Midi, Is.EqualTo(69));
            Assert.That(package.Manifest.Visuals.Video, Is.Not.Null);
            Assert.That(package.GetAsset(package.Manifest.Visuals.Video!).ToArray(), Is.EqualTo(new byte[] { 0, 1, 2, 3 }));
        });
    }

    [Test]
    public void TestRejectsPathTraversal()
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, true))
        {
            using Stream entry = archive.CreateEntry("../escape").Open();
            entry.WriteByte(1);
        }

        stream.Position = 0;
        Assert.That(() => UtzPackage.Open(stream), Throws.TypeOf<InvalidDataException>());
    }

    internal static MemoryStream CreatePackage()
    {
        var files = new Dictionary<string, byte[]>
        {
            ["audio/instrumental.mp3"] = new byte[] { 10, 20, 30 },
            ["audio/original.mp3"] = new byte[] { 30, 20, 10 },
            ["charts/transcript.json"] = Encoding.UTF8.GetBytes("{\"language\":\"ja\",\"segments\":[{\"text\":\"test\",\"start\":0.0,\"end\":1.0,\"words\":[]}]}"),
            ["charts/pitch-track.json"] = Encoding.UTF8.GetBytes("{\"format_version\":1,\"model\":null,\"hop_seconds\":0.01,\"frames\":[]}"),
            ["charts/pitch-notes.json"] = Encoding.UTF8.GetBytes("{\"format_version\":1,\"notes\":[{\"start\":0.0,\"end\":1.0,\"midi\":69,\"confidence\":1.0,\"kind\":\"golden\"}]}"),
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
            song = new
            {
                title = "God knows...",
                artist = "SOS Brigade",
                duration_seconds = 1.0,
                key = "G#m",
            },
            audio = new
            {
                instrumental = asset("audio/instrumental.mp3", "audio/mpeg"),
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
            scoring = new
            {
                engine = "uta.pitch",
                version = 1,
                octave_tolerance = false,
            },
        };

        var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, true))
        {
            addEntry(archive, "manifest.json", Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(manifest)));
            foreach (var (path, bytes) in files)
                addEntry(archive, path, bytes);
        }

        output.Position = 0;
        return output;
    }

    private static void addEntry(ZipArchive archive, string path, byte[] bytes)
    {
        using Stream stream = archive.CreateEntry(path).Open();
        stream.Write(bytes);
    }
}
