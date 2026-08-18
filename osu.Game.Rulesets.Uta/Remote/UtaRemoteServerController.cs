// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENSE file in the repository root for full licence text.

using System;
using System.Threading;
using System.Threading.Tasks;
using osu.Framework.Bindables;

namespace osu.Game.Rulesets.Uta.Remote;

public enum UtaRemoteServerState
{
    Stopped,
    Starting,
    WaitingForClient,
    Connected,
    Stopping,
    Faulted,
}

public enum UtaRemoteServerStartReason
{
    RemoteOverlayOpened,
    ExplicitStart,
    ImmersiveQueueGameplayStarted,
}

public enum UtaRemoteServerStopReason
{
    ExplicitStop,
    NoAuthenticatedClientTimeout,
    Disposed,
}

public sealed class UtaRemoteServerController : IDisposable
{
    public static readonly TimeSpan IDLE_TIMEOUT = TimeSpan.FromSeconds(90);

    private readonly IUtaRemoteCommandTarget commandTarget;
    private readonly Func<UtaRemoteSnapshot> snapshotProvider;
    private readonly SemaphoreSlim lifecycle = new(1, 1);
    private readonly int port;
    private UtaRemoteServer? server;
    private Timer? idleTimer;
    private DateTimeOffset idleDeadline;
    private long generation;
    private bool disposed;

    public Bindable<UtaRemoteServerState> State { get; } = new(UtaRemoteServerState.Stopped);
    public BindableInt AuthenticatedClientCount { get; } = new();
    public Bindable<TimeSpan?> IdleShutdownRemaining { get; } = new();
    public Bindable<string?> LastError { get; } = new();
    public UtaRemoteServer? Server => server;
    public event Action? Changed;

    public UtaRemoteServerController(IUtaRemoteCommandTarget commandTarget, Func<UtaRemoteSnapshot> snapshotProvider, int port = UtaRemoteNetworkPolicy.DEFAULT_PORT)
    {
        this.commandTarget = commandTarget;
        this.snapshotProvider = snapshotProvider;
        this.port = port;
    }

    public async Task EnsureStartedAsync(UtaRemoteServerStartReason reason)
    {
        await lifecycle.WaitAsync().ConfigureAwait(false);
        try
        {
            if (server?.IsRunning == true)
                return;

            State.Value = UtaRemoteServerState.Starting;
            LastError.Value = null;
            long startedGeneration = Interlocked.Increment(ref generation);
            var created = new UtaRemoteServer(commandTarget, snapshotProvider);
            created.StatusChanged += onServerStatusChanged;
            server = created;
            try
            {
                await created.StartAsync(port).ConfigureAwait(false);
                State.Value = UtaRemoteServerState.WaitingForClient;
                beginIdleCountdown(startedGeneration);
            }
            catch (Exception exception)
            {
                created.StatusChanged -= onServerStatusChanged;
                await created.DisposeAsync().ConfigureAwait(false);
                if (ReferenceEquals(server, created))
                    server = null;
                LastError.Value = exception.GetBaseException().Message;
                State.Value = UtaRemoteServerState.Faulted;
            }
            Changed?.Invoke();
        }
        finally
        {
            lifecycle.Release();
        }
    }

    public async Task StopAsync(UtaRemoteServerStopReason reason)
    {
        await lifecycle.WaitAsync().ConfigureAwait(false);
        try
        {
            Interlocked.Increment(ref generation);
            idleTimer?.Dispose();
            idleTimer = null;
            IdleShutdownRemaining.Value = null;
            UtaRemoteServer? stopping = server;
            server = null;
            if (stopping == null)
            {
                State.Value = UtaRemoteServerState.Stopped;
                return;
            }

            State.Value = UtaRemoteServerState.Stopping;
            stopping.StatusChanged -= onServerStatusChanged;
            await stopping.DisposeAsync().ConfigureAwait(false);
            AuthenticatedClientCount.Value = 0;
            State.Value = UtaRemoteServerState.Stopped;
            Changed?.Invoke();
        }
        finally
        {
            lifecycle.Release();
        }
    }

    public void DisconnectAllClients()
    {
        server?.RevokeAll();
        refreshClientState();
    }

    public void BroadcastQueue(UtaRemoteQueueMessage message) => server?.BroadcastQueue(message);

    private void onServerStatusChanged()
    {
        refreshClientState();
        Changed?.Invoke();
    }

    private void refreshClientState()
    {
        int count = server?.ActiveClientCount ?? 0;
        int previous = AuthenticatedClientCount.Value;
        AuthenticatedClientCount.Value = count;
        if (count > 0)
        {
            idleTimer?.Dispose();
            idleTimer = null;
            IdleShutdownRemaining.Value = null;
            State.Value = UtaRemoteServerState.Connected;
        }
        else if (server?.IsRunning == true && previous > 0)
        {
            State.Value = UtaRemoteServerState.WaitingForClient;
            beginIdleCountdown(Volatile.Read(ref generation));
        }
    }

    private void beginIdleCountdown(long timerGeneration)
    {
        idleDeadline = DateTimeOffset.UtcNow + IDLE_TIMEOUT;
        idleTimer?.Dispose();
        idleTimer = new Timer(_ => idleTick(timerGeneration), null, TimeSpan.Zero, TimeSpan.FromSeconds(1));
    }

    private void idleTick(long timerGeneration)
    {
        if (timerGeneration != Volatile.Read(ref generation) || server?.IsRunning != true)
            return;
        if (server.ActiveClientCount > 0)
        {
            refreshClientState();
            return;
        }

        TimeSpan remaining = idleDeadline - DateTimeOffset.UtcNow;
        IdleShutdownRemaining.Value = remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
        Changed?.Invoke();
        if (remaining <= TimeSpan.Zero)
            _ = StopAsync(UtaRemoteServerStopReason.NoAuthenticatedClientTimeout);
    }

    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;
        StopAsync(UtaRemoteServerStopReason.Disposed).GetAwaiter().GetResult();
        lifecycle.Dispose();
    }
}
