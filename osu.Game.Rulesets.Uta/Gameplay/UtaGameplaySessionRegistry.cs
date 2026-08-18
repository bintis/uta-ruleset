// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENSE file in the repository root for full licence text.

using System;
using osu.Framework;
using osu.Game.Rulesets.Uta.Remote;

namespace osu.Game.Rulesets.Uta.Gameplay;

public interface IUtaGameplaySession : IUtaRemoteCommandTarget
{
    Guid BeatmapId { get; }
    UtaRemoteSnapshot Snapshot { get; }
}

public sealed class UtaGameplaySessionRegistry
{
    private readonly object sync = new();
    private long generation;
    private GameplayLease? current;

    public event Action? Changed;

    public GameplayLease? Current
    {
        get
        {
            lock (sync)
                return current;
        }
    }

    public GameplayLease Attach(IUtaGameplaySession session)
    {
        GameplayLease lease;
        lock (sync)
            current = lease = new GameplayLease(++generation, session, detach);
        osu.Framework.Logging.Logger.Log($"Uta gameplay session attached: generation={lease.Generation} beatmap={session.BeatmapId}");
        Changed?.Invoke();
        return lease;
    }

    public bool IsCurrent(long leaseGeneration)
    {
        lock (sync)
            return current?.Generation == leaseGeneration;
    }

    private void detach(GameplayLease lease)
    {
        bool changed = false;
        lock (sync)
        {
            if (current?.Generation == lease.Generation)
            {
                current = null;
                changed = true;
            }
        }
        if (changed)
            Changed?.Invoke();
    }
}

public sealed class GameplayLease : IDisposable
{
    private readonly Action<GameplayLease> detach;
    private bool disposed;

    public long Generation { get; }
    public IUtaGameplaySession Session { get; }

    internal GameplayLease(long generation, IUtaGameplaySession session, Action<GameplayLease> detach)
    {
        Generation = generation;
        Session = session;
        this.detach = detach;
    }

    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;
        detach(this);
    }
}
