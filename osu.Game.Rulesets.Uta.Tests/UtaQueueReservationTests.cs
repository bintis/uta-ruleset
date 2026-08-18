// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENSE file in the repository root for full licence text.

using System;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Uta.Mods;
using osu.Game.Rulesets.Uta.Playback;
using osu.Game.Rulesets.Uta.Queue;
using osu.Game.Rulesets.Uta.Remote;

namespace osu.Game.Rulesets.Uta.Tests;

[TestFixture]
public sealed class UtaQueueReservationTests
{
    [Test]
    public void TestQueueAddPersistsAndReloadsReservationOptions()
    {
        string path = Path.Combine(Path.GetTempPath(), $"uta-queue-{Guid.NewGuid():N}.json");

        try
        {
            using (var queue = new UtaSongQueueService(path))
            {
                QueueMutationResult added = queue.Add(new UtaSongRequest(
                    Guid.NewGuid(), "Song", "Artist", "Expert", 180_000,
                    UtaQueueRequestSource.RemoteController,
                    Options: new UtaQueuePlaybackOptions(1.1, 2, new[] { "NF", "PR" })));

                Assert.That(added.Succeeded, Is.True);
                UtaSongQueueEntry entry = queue.GetSnapshot().Single();
                Assert.That(entry.Playback.Speed, Is.EqualTo(1.1));
                Assert.That(entry.Playback.Transpose, Is.EqualTo(2));
                Assert.That(entry.Playback.ModList, Is.EqualTo(new[] { "NF", "PR" }));
            }

            using var reloaded = new UtaSongQueueService(path);
            UtaSongQueueEntry restored = reloaded.GetSnapshot().Single();
            Assert.That(restored.Playback.Speed, Is.EqualTo(1.1));
            Assert.That(restored.Playback.Transpose, Is.EqualTo(2));
            Assert.That(restored.Playback.ModList, Is.EqualTo(new[] { "NF", "PR" }));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public void TestVersion1QueueFileLoadsWithDefaultOptions()
    {
        string path = Path.Combine(Path.GetTempPath(), $"uta-queue-{Guid.NewGuid():N}.json");
        Guid beatmapId = Guid.NewGuid();
        Guid entryId = Guid.NewGuid();
        File.WriteAllText(path,
            "{\"Version\":1,\"SavedAt\":\"2026-01-01T00:00:00+00:00\",\"Entries\":[{\"EntryId\":\""
            + entryId + "\",\"BeatmapId\":\"" + beatmapId
            + "\",\"Title\":\"Old\",\"Artist\":\"A\",\"DifficultyName\":\"Easy\",\"LengthMs\":1000,\"RequestedAt\":\"2026-01-01T00:00:00+00:00\",\"Source\":0,\"RequestedByClientId\":null,\"State\":0}]}");

        try
        {
            using var queue = new UtaSongQueueService(path);
            UtaSongQueueEntry entry = queue.GetSnapshot().Single();
            Assert.That(entry.Title, Is.EqualTo("Old"));
            Assert.That(entry.Playback.Speed, Is.EqualTo(1));
            Assert.That(entry.Playback.Transpose, Is.EqualTo(0));
            Assert.That(entry.Playback.ModList, Is.Empty);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public void TestConfigureUpdatesQueuedEntryAndRejectsUnknownMods()
    {
        string path = Path.Combine(Path.GetTempPath(), $"uta-queue-{Guid.NewGuid():N}.json");

        try
        {
            using var queue = new UtaSongQueueService(path);
            queue.Add(new UtaSongRequest(Guid.NewGuid(), "Song", "Artist", "Easy", 1000, UtaQueueRequestSource.LocalOverlay));
            Guid id = queue.GetSnapshot().Single().EntryId;

            Assert.That(queue.Configure(id, new UtaQueuePlaybackOptions(0.9, -3, new[] { "RX" })).Succeeded, Is.True);
            Assert.That(queue.GetSnapshot().Single().Playback.Transpose, Is.EqualTo(-3));
            Assert.That(queue.Configure(id, new UtaQueuePlaybackOptions(1, 0, new[] { "ZZ" })).Succeeded, Is.False);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public void TestQueueAddOptionsParseAndSpectatorCannotConfigure()
    {
        byte[] add = Encoding.UTF8.GetBytes(
            """{"type":"command","sequence":1,"command":"queueAdd","text":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","options":{"speed":1.2,"transpose":-2,"mods":["NF","IQ"]}}""");
        Assert.That(UtaRemoteProtocol.TryParseCommand(add, UtaRemoteRole.Spectator, out UtaRemoteCommand? parsed, out string error), Is.True, error);
        Assert.That(parsed!.Options, Is.Not.Null);
        Assert.That(parsed.Options!.Speed, Is.EqualTo(1.2));
        Assert.That(parsed.Options.Transpose, Is.EqualTo(-2));
        Assert.That(parsed.Options.ModList, Is.EqualTo(new[] { "NF", "IQ" }));

        byte[] configure = Encoding.UTF8.GetBytes(
            """{"type":"command","sequence":2,"command":"queueConfigure","text":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","options":{"speed":1}}""");
        Assert.That(UtaRemoteProtocol.TryParseCommand(configure, UtaRemoteRole.Spectator, out _, out string spectatorError), Is.False);
        Assert.That(spectatorError, Does.Contain("read-only"));

        byte[] invalid = Encoding.UTF8.GetBytes(
            """{"type":"command","sequence":3,"command":"queueAdd","text":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","options":{"speed":3}}""");
        Assert.That(UtaRemoteProtocol.TryParseCommand(invalid, UtaRemoteRole.Controller, out _, out _), Is.False);
    }

    [Test]
    public void TestEmptyReservationModsKeepAutoplayAndImmersiveQueue()
    {
        var current = new Mod[] { new UtaModAutoplay(), new UtaModImmersiveQueue(), new UtaModNightcore() };
        var options = new UtaQueuePlaybackOptions(1.1, 0, Array.Empty<string>());

        Assert.That(UtaPlaybackCoordinator.TryComposeReservationMods(options, current, out var next, out string error), Is.True, error);
        Assert.That(next.Any(mod => mod is UtaModAutoplay), Is.True);
        Assert.That(next.Any(mod => mod is UtaModImmersiveQueue), Is.True);
        Assert.That(next.Any(mod => mod is UtaModNightcore), Is.True);
        Assert.That(next.OfType<UtaModTranspose>(), Is.Empty);
    }

    [Test]
    public void TestComposeReservationModsReplacesRemoteSetAndKeepsForeignMods()
    {
        var current = new Mod[] { new UtaModNoFail(), new UtaModNightcore() };
        var options = new UtaQueuePlaybackOptions(1, 2, new[] { "PR", "OCT" });

        Assert.That(UtaPlaybackCoordinator.TryComposeReservationMods(options, current, out var next, out string error), Is.True, error);
        Assert.That(next.Any(mod => mod is UtaModNightcore), Is.True);
        Assert.That(next.Any(mod => mod is UtaModNoFail), Is.False);
        Assert.That(next.Any(mod => mod is UtaModPractice), Is.True);
        Assert.That(next.Any(mod => mod is UtaModOctaveFold), Is.True);
        Assert.That(next.OfType<UtaModTranspose>().Single().Semitones, Is.EqualTo(2));
    }

    [Test]
    public void TestWireCommandIdsCoverTheFullRemoteSurface()
    {
        string[] names =
        {
            UtaRemoteCommands.Ping, UtaRemoteCommands.Play, UtaRemoteCommands.Pause,
            UtaRemoteCommands.TogglePlayback, UtaRemoteCommands.Seek, UtaRemoteCommands.SeekRelative,
            UtaRemoteCommands.Speed, UtaRemoteCommands.SetLoopA, UtaRemoteCommands.SetLoopB,
            UtaRemoteCommands.ClearLoop, UtaRemoteCommands.PreviousPhrase, UtaRemoteCommands.NextPhrase,
            UtaRemoteCommands.RetryPhrase, UtaRemoteCommands.LoopPhrase,
            UtaRemoteCommands.BackgroundMusicVolume, UtaRemoteCommands.OriginalVocalsVolume,
            UtaRemoteCommands.MicrophoneMonitorVolume, UtaRemoteCommands.Transpose,
            UtaRemoteCommands.OctaveFold, UtaRemoteCommands.OriginalVocals,
            UtaRemoteCommands.MicrophoneLatency, UtaRemoteCommands.AccompanimentLatency,
            UtaRemoteCommands.LyricsLatency, UtaRemoteCommands.Disconnect,
            UtaRemoteCommands.LibrarySearch, UtaRemoteCommands.QueueAdd, UtaRemoteCommands.QueueRemove,
            UtaRemoteCommands.QueueClear, UtaRemoteCommands.QueuePlayNow, UtaRemoteCommands.SkipCurrent,
            UtaRemoteCommands.SkipToNext, UtaRemoteCommands.QueueAddNext, UtaRemoteCommands.QueueMove,
            UtaRemoteCommands.QueueMoveToTop, UtaRemoteCommands.QueueMoveToBottom,
            UtaRemoteCommands.AutoAdvance, UtaRemoteCommands.SetMod, UtaRemoteCommands.QueueConfigure,
        };

        foreach (string name in names)
        {
            byte id = UtaRemoteWire.IdOf(name);
            Assert.That(id, Is.GreaterThan(0), name);
            Assert.That(UtaRemoteWire.NameOf(id), Is.EqualTo(name));
        }
    }

    [Test]
    public void TestSkipToNextIsAKnownControllerCommand()
    {
        byte[] command = Encoding.UTF8.GetBytes("{\"type\":\"command\",\"sequence\":1,\"command\":\"skipToNext\"}");
        Assert.That(UtaRemoteProtocol.TryParseCommand(command, UtaRemoteRole.Controller, out UtaRemoteCommand? parsed, out string error), Is.True, error);
        Assert.That(parsed!.Name, Is.EqualTo(UtaRemoteCommands.SkipToNext));
        Assert.That(UtaRemoteProtocol.TryParseCommand(command, UtaRemoteRole.Spectator, out _, out _), Is.False);
    }

    [Test]
    public void TestClearRemovesReservedEntries()
    {
        string path = Path.Combine(Path.GetTempPath(), $"uta-queue-{Guid.NewGuid():N}.json");

        try
        {
            using var queue = new UtaSongQueueService(path);
            queue.Add(new UtaSongRequest(Guid.NewGuid(), "Song", "Artist", "Easy", 1000, UtaQueueRequestSource.LocalOverlay));
            Assert.That(queue.ReserveNext(), Is.Not.Null);
            Assert.That(queue.GetSnapshot().Single().State, Is.EqualTo(UtaQueueEntryState.Reserved));
            Assert.That(queue.Clear().Succeeded, Is.True);
            Assert.That(queue.GetSnapshot(), Is.Empty);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public void TestComposeReservationModsRejectsUnknownAcronym()
    {
        Assert.That(UtaPlaybackCoordinator.TryComposeReservationMods(
            new UtaQueuePlaybackOptions(1, 0, new[] { "HD" }),
            Array.Empty<Mod>(),
            out _,
            out _), Is.False);
    }
}
