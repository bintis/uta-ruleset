// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENSE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using osu.Framework.Bindables;
using osu.Game.Rulesets.Uta.Storage;

namespace osu.Game.Rulesets.Uta.Queue;

public enum UtaQueueRequestSource
{
    LocalOverlay,
    SongSelectContextMenu,
    RemoteController,
    RemoteSpectator,
}

public enum UtaQueueEntryState
{
    Queued,
    Reserved,
    Unavailable,
    Failed,
}

public sealed record UtaSongQueueEntry(
    Guid EntryId,
    Guid BeatmapId,
    string Title,
    string Artist,
    string DifficultyName,
    long LengthMs,
    DateTimeOffset RequestedAt,
    UtaQueueRequestSource Source,
    string? RequestedByClientId,
    UtaQueueEntryState State);

public sealed record UtaSongRequest(
    Guid BeatmapId,
    string Title,
    string Artist,
    string DifficultyName,
    long LengthMs,
    UtaQueueRequestSource Source,
    string? RequestedByClientId = null);

public sealed record QueueMutationResult(bool Succeeded, string? Error = null, int Position = -1)
{
    public static QueueMutationResult Reject(string error) => new(false, error);
}

public sealed record QueueReservation(Guid EntryId, Guid Token);

public sealed class UtaSongQueueService : IDisposable
{
    public const int MAXIMUM_ENTRIES = 500;
    private const int maximum_file_bytes = 2 * 1024 * 1024;

    private readonly object sync = new();
    private readonly List<UtaSongQueueEntry> entries = new();
    private readonly BindableLong revision = new();
    private readonly SemaphoreSlim persistenceGate = new(1, 1);
    private Timer? saveTimer;
    private long saveGeneration;
    private bool disposed;

    public event Action? Changed;
    public IBindable<long> Revision => revision;

    public UtaSongQueueService() => load();

    public IReadOnlyList<UtaSongQueueEntry> GetSnapshot()
    {
        lock (sync)
            return entries.ToArray();
    }

    public QueueMutationResult Add(UtaSongRequest request)
    {
        int position;
        lock (sync)
        {
            if (entries.Count >= MAXIMUM_ENTRIES)
                return QueueMutationResult.Reject("The queue is full.");

            entries.Add(new UtaSongQueueEntry(
                Guid.NewGuid(), request.BeatmapId, request.Title, request.Artist, request.DifficultyName,
                request.LengthMs, DateTimeOffset.UtcNow, request.Source, request.RequestedByClientId, UtaQueueEntryState.Queued));
            position = entries.Count;
        }

        changed();
        return new QueueMutationResult(true, Position: position);
    }

    public QueueMutationResult Remove(Guid entryId) => mutate(() =>
    {
        int index = entries.FindIndex(entry => entry.EntryId == entryId);
        if (index < 0)
            return false;
        entries.RemoveAt(index);
        return true;
    }, "The queue entry no longer exists.");

    public QueueMutationResult Move(Guid entryId, int newIndex) => mutate(() =>
    {
        int oldIndex = entries.FindIndex(entry => entry.EntryId == entryId);
        if (oldIndex < 0 || entries[oldIndex].State == UtaQueueEntryState.Reserved)
            return false;
        UtaSongQueueEntry entry = entries[oldIndex];
        entries.RemoveAt(oldIndex);
        entries.Insert(Math.Clamp(newIndex, 0, entries.Count), entry);
        return true;
    }, "The queue entry cannot be moved.");

    public QueueMutationResult MoveToTop(Guid entryId) => Move(entryId, 0);
    public QueueMutationResult MoveToBottom(Guid entryId) => Move(entryId, int.MaxValue);

    public QueueMutationResult Clear() => mutate(() =>
    {
        int removed = entries.RemoveAll(entry => entry.State != UtaQueueEntryState.Reserved);
        return removed > 0;
    }, "The queue is already empty.");

    public QueueMutationResult MarkUnavailable(Guid entryId) => mutate(() => replaceState(entryId, UtaQueueEntryState.Unavailable), "The queue entry no longer exists.");

    public QueueReservation? Reserve(Guid entryId)
    {
        QueueReservation? reservation = null;
        QueueMutationResult result = mutate(() =>
        {
            int index = entries.FindIndex(entry => entry.EntryId == entryId && entry.State == UtaQueueEntryState.Queued);
            if (index < 0)
                return false;
            entries[index] = entries[index] with { State = UtaQueueEntryState.Reserved };
            reservation = new QueueReservation(entryId, Guid.NewGuid());
            return true;
        }, "The queue entry is not available.");
        return result.Succeeded ? reservation : null;
    }

    public QueueReservation? ReserveNext()
    {
        Guid? id;
        lock (sync)
            id = entries.FirstOrDefault(entry => entry.State == UtaQueueEntryState.Queued)?.EntryId;
        return id == null ? null : Reserve(id.Value);
    }

    public void Commit(QueueReservation reservation) => Remove(reservation.EntryId);

    public void Release(QueueReservation reservation)
        => mutate(() => replaceState(reservation.EntryId, UtaQueueEntryState.Queued, UtaQueueEntryState.Reserved), "The reservation is stale.");

    private bool replaceState(Guid entryId, UtaQueueEntryState state, UtaQueueEntryState? requiredState = null)
    {
        int index = entries.FindIndex(entry => entry.EntryId == entryId && (requiredState == null || entry.State == requiredState));
        if (index < 0)
            return false;
        entries[index] = entries[index] with { State = state };
        return true;
    }

    private QueueMutationResult mutate(Func<bool> mutation, string error)
    {
        bool changedValue;
        lock (sync)
            changedValue = mutation();
        if (!changedValue)
            return QueueMutationResult.Reject(error);
        changed();
        return new QueueMutationResult(true);
    }

    private void changed()
    {
        revision.Value++;
        scheduleSave();
        Changed?.Invoke();
    }

    private void load()
    {
        string path = UtaStoragePaths.QueueFile;
        if (!File.Exists(path))
            return;

        try
        {
            var info = new FileInfo(path);
            if (info.Length > maximum_file_bytes)
                throw new InvalidDataException("Queue file exceeds the size limit.");

            QueueFile? file = JsonSerializer.Deserialize<QueueFile>(File.ReadAllText(path));
            if (file?.Version != 1 || file.Entries.Count > MAXIMUM_ENTRIES)
                throw new InvalidDataException("Unsupported or oversized queue file.");

            entries.AddRange(file.Entries.Select(entry => entry.State == UtaQueueEntryState.Reserved
                ? entry with { State = UtaQueueEntryState.Queued }
                : entry));
        }
        catch
        {
            try
            {
                File.Move(path, path + ".corrupt", true);
            }
            catch
            {
            }
            entries.Clear();
        }
    }

    private void scheduleSave()
    {
        long generation = Interlocked.Increment(ref saveGeneration);
        saveTimer ??= new Timer(_ => _ = saveAsync(Volatile.Read(ref saveGeneration)), null, Timeout.Infinite, Timeout.Infinite);
        saveTimer.Change(300, Timeout.Infinite);
    }

    private async Task saveAsync(long generation)
    {
        await persistenceGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (generation != Volatile.Read(ref saveGeneration) || disposed)
                return;

            UtaSongQueueEntry[] snapshot;
            lock (sync)
                snapshot = entries.ToArray();

            string path = UtaStoragePaths.QueueFile;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            string temporary = path + ".tmp";
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(new QueueFile(1, DateTimeOffset.UtcNow, snapshot))).ConfigureAwait(false);
            if (generation == Volatile.Read(ref saveGeneration))
                File.Move(temporary, path, true);
        }
        catch
        {
        }
        finally
        {
            persistenceGate.Release();
        }
    }

    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;
        Interlocked.Increment(ref saveGeneration);
        saveTimer?.Dispose();

        persistenceGate.Wait();
        try
        {
            UtaSongQueueEntry[] snapshot;
            lock (sync)
                snapshot = entries.ToArray();

            string path = UtaStoragePaths.QueueFile;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            string temporary = path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(new QueueFile(1, DateTimeOffset.UtcNow, snapshot)));
            File.Move(temporary, path, true);
        }
        catch
        {
        }
        finally
        {
            persistenceGate.Release();
        }
    }

    private sealed record QueueFile(int Version, DateTimeOffset SavedAt, IReadOnlyList<UtaSongQueueEntry> Entries);
}
