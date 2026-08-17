// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENSE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace osu.Game.Rulesets.Uta.Recording;

/// <summary>
/// Filesystem-backed phrase attempt manager. Every retry is a separate monotonic
/// WAV take; backward practice seeks are never spliced into one ambiguous file.
/// </summary>
public sealed class UtaPhraseTakeManager : IAsyncDisposable
{
    private readonly string rootDirectory;
    private readonly List<UtaPhraseTakeEntry> takes = new();
    private UtaRecordingSession? activeSession;
    private UtaPhraseTakeEntry? activeEntry;

    public IReadOnlyList<UtaPhraseTakeEntry> Takes => takes.ToArray();
    public Guid? SelectedTakeId { get; private set; }
    public IUtaPcmCaptureSink? ActiveSink => activeSession;

    public UtaPhraseTakeManager(string rootDirectory)
    {
        if (string.IsNullOrWhiteSpace(rootDirectory))
            throw new ArgumentException("Phrase take root is required.", nameof(rootDirectory));

        this.rootDirectory = Path.GetFullPath(rootDirectory);
        Directory.CreateDirectory(this.rootDirectory);
    }

    public UtaPhraseTakeEntry StartAttempt(
        int phraseIndex,
        long phraseStartTimeMicroseconds,
        long phraseEndTimeMicroseconds,
        UtaRecordingMetadata metadata)
    {
        if (activeSession != null)
            throw new InvalidOperationException("Finish the active phrase take before starting another.");
        if (phraseIndex < 0)
            throw new ArgumentOutOfRangeException(nameof(phraseIndex));
        if (phraseEndTimeMicroseconds <= phraseStartTimeMicroseconds)
            throw new ArgumentOutOfRangeException(nameof(phraseEndTimeMicroseconds));

        Guid takeId = metadata.TakeId == Guid.Empty ? Guid.NewGuid() : metadata.TakeId;
        metadata.TakeId = takeId;
        string directory = Path.Combine(rootDirectory, takeId.ToString("D"));
        Directory.CreateDirectory(directory);

        var entry = new UtaPhraseTakeEntry
        {
            TakeId = takeId,
            PhraseIndex = phraseIndex,
            PhraseStartTimeMicroseconds = phraseStartTimeMicroseconds,
            PhraseEndTimeMicroseconds = phraseEndTimeMicroseconds,
            DirectoryPath = directory,
            RecordingFileName = "take.wav",
        };

        activeSession = new UtaRecordingSession();
        activeSession.StartDeferred(Path.Combine(directory, entry.RecordingFileName), metadata);
        activeEntry = entry;
        return entry;
    }

    public async Task<UtaPhraseTakeEntry?> FinishActiveAttemptAsync()
    {
        if (activeSession == null || activeEntry == null)
            return null;

        UtaRecordingSession session = activeSession;
        UtaPhraseTakeEntry entry = activeEntry;
        activeSession = null;
        activeEntry = null;

        UtaRecordingMetadata metadata = await session.StopAsync().ConfigureAwait(false);
        await session.DisposeAsync().ConfigureAwait(false);

        entry.Metadata = metadata;
        entry.Complete = metadata.Complete;
        entry.FaultReason = metadata.FaultReason;

        string temporary = Path.Combine(entry.DirectoryPath, "take.json.tmp");
        string final = Path.Combine(entry.DirectoryPath, "take.json");
        await File.WriteAllTextAsync(
            temporary,
            JsonSerializer.Serialize(entry, new JsonSerializerOptions { WriteIndented = true })).ConfigureAwait(false);
        File.Move(temporary, final, true);
        await File.WriteAllTextAsync(Path.Combine(entry.DirectoryPath, "complete"), string.Empty).ConfigureAwait(false);

        takes.Add(entry);
        if (SelectedTakeId == null && entry.Complete)
            Select(entry.TakeId);

        return entry;
    }

    public void Select(Guid takeId)
    {
        if (takes.All(t => t.TakeId != takeId))
            throw new ArgumentException("Unknown phrase take.", nameof(takeId));

        SelectedTakeId = takeId;
        string temp = Path.Combine(rootDirectory, "selected.tmp");
        string final = Path.Combine(rootDirectory, "selected");
        File.WriteAllText(temp, takeId.ToString("D"));
        File.Move(temp, final, true);
    }

    public bool Delete(Guid takeId)
    {
        if (activeEntry?.TakeId == takeId)
            throw new InvalidOperationException("Cannot delete the take currently being recorded.");

        UtaPhraseTakeEntry? entry = takes.FirstOrDefault(t => t.TakeId == takeId);
        if (entry == null)
            return false;

        Directory.Delete(entry.DirectoryPath, true);
        takes.Remove(entry);

        if (SelectedTakeId == takeId)
        {
            SelectedTakeId = takes.LastOrDefault(t => t.Complete)?.TakeId;
            string selection = Path.Combine(rootDirectory, "selected");
            if (SelectedTakeId == null)
            {
                if (File.Exists(selection))
                    File.Delete(selection);
            }
            else
            {
                File.WriteAllText(selection + ".tmp", SelectedTakeId.Value.ToString("D"));
                File.Move(selection + ".tmp", selection, true);
            }
        }

        return true;
    }

    public async ValueTask DisposeAsync()
    {
        await FinishActiveAttemptAsync().ConfigureAwait(false);
    }
}

public sealed class UtaPhraseTakeEntry
{
    public Guid TakeId { get; set; }
    public int PhraseIndex { get; set; }
    public long PhraseStartTimeMicroseconds { get; set; }
    public long PhraseEndTimeMicroseconds { get; set; }
    public string DirectoryPath { get; set; } = string.Empty;
    public string RecordingFileName { get; set; } = "take.wav";
    public bool Complete { get; set; }
    public UtaRecordingFaultReason FaultReason { get; set; }
    public UtaRecordingMetadata Metadata { get; set; } = new();
}
