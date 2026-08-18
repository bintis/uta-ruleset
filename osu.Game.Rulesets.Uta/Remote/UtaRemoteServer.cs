// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENSE file in the repository root for full licence text.

using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.WebSockets;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace osu.Game.Rulesets.Uta.Remote;

/// <summary>
/// Optional gameplay-scoped HTTP/WebSocket endpoint for the bundled mobile remote.
/// Nothing listens until <see cref="StartAsync"/> is called by an explicit desktop action.
/// </summary>
public sealed class UtaRemoteServer : IAsyncDisposable
{
    private const int maximum_clients = 8;
    private const int snapshot_interval_ms = 100;

    private readonly IUtaRemoteCommandTarget commandTarget;
    private readonly Func<UtaRemoteSnapshot> snapshotProvider;
    private readonly UtaRemoteCredentialStore credentials = new();
    private readonly ConcurrentDictionary<string, ClientConnection> clients = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim lifecycleGate = new(1, 1);
    private readonly object statusSync = new();

    private HttpListener? listener;
    private CancellationTokenSource? lifetimeCancellation;
    private Task? acceptTask;
    private Task? broadcastTask;
    private HashSet<string> allowedHosts = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<IPAddress> listenAddresses = Array.Empty<IPAddress>();
    private int port;
    private string? statusMessage;

    public event Action? StatusChanged;

    public bool IsRunning => listener?.IsListening == true;
    public int Port => port;
    public IReadOnlyList<IPAddress> ListenAddresses => listenAddresses;
    public int ActiveClientCount => clients.Count;
    public string? StatusMessage
    {
        get
        {
            lock (statusSync)
                return statusMessage;
        }
    }

    public UtaRemoteServer(IUtaRemoteCommandTarget commandTarget, Func<UtaRemoteSnapshot> snapshotProvider)
    {
        this.commandTarget = commandTarget ?? throw new ArgumentNullException(nameof(commandTarget));
        this.snapshotProvider = snapshotProvider ?? throw new ArgumentNullException(nameof(snapshotProvider));
    }

    public async Task StartAsync(int requestedPort = UtaRemoteNetworkPolicy.DEFAULT_PORT, CancellationToken cancellationToken = default)
    {
        if (requestedPort is <= 0 or > ushort.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(requestedPort));

        await lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (IsRunning)
                return;

            IReadOnlyList<IPAddress> addresses = UtaRemoteNetworkPolicy.GetPrivateListenAddresses();
            var created = new HttpListener
            {
                IgnoreWriteExceptions = true,
            };

            foreach (IPAddress address in addresses)
                created.Prefixes.Add(UtaRemoteNetworkPolicy.ToHttpUrl(address, requestedPort));

            try
            {
                created.Start();
            }
            catch
            {
                created.Close();
                throw;
            }

            listenAddresses = addresses;
            port = requestedPort;
            allowedHosts = addresses.Select(address => address.ToString())
                                    .Append("localhost")
                                    .Append(Dns.GetHostName())
                                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
            listener = created;
            lifetimeCancellation = new CancellationTokenSource();
            CancellationToken lifetime = lifetimeCancellation.Token;
            acceptTask = acceptLoopAsync(created, lifetime);
            broadcastTask = broadcastLoopAsync(lifetime);
            setStatus($"Listening on {string.Join(", ", addresses.Select(address => UtaRemoteNetworkPolicy.ToHttpUrl(address, requestedPort)))}");
        }
        finally
        {
            lifecycleGate.Release();
        }
    }

    public UtaRemotePairingTicket CreatePairingTicket(UtaRemoteRole role)
    {
        if (!IsRunning)
            throw new InvalidOperationException("Remote service is not running.");

        UtaRemotePairingTicket ticket = credentials.IssuePairingTicket(role);
        setStatus($"{role} pairing ticket active until {ticket.ExpiresAt:HH:mm:ss} UTC");
        return ticket;
    }

    public string GetPairingUrl(UtaRemotePairingTicket ticket)
    {
        IPAddress address = listenAddresses.FirstOrDefault(value => !IPAddress.IsLoopback(value))
                            ?? listenAddresses.FirstOrDefault()
                            ?? IPAddress.Loopback;
        return $"{UtaRemoteNetworkPolicy.ToHttpUrl(address, port)}#ticket={Uri.EscapeDataString(ticket.Token)}&role={ticket.Role.ToString().ToLowerInvariant()}";
    }

    public bool Revoke(string sessionId)
    {
        bool revoked = credentials.Revoke(sessionId);
        if (clients.TryRemove(sessionId, out ClientConnection? client))
            _ = client.CloseAsync(WebSocketCloseStatus.PolicyViolation, "Session revoked", CancellationToken.None);
        if (revoked)
            setStatus("Remote session revoked.");
        return revoked;
    }

    public int RevokeAll()
    {
        int revoked = credentials.RevokeAll();
        foreach ((string id, ClientConnection client) in clients.ToArray())
        {
            clients.TryRemove(id, out _);
            _ = client.CloseAsync(WebSocketCloseStatus.PolicyViolation, "All sessions revoked", CancellationToken.None);
        }

        setStatus(revoked == 0 ? "No paired remote sessions." : $"Revoked {revoked} remote session(s).");
        return revoked;
    }

    public IReadOnlyList<UtaRemoteSession> SnapshotSessions() => credentials.SnapshotSessions();

    public void BroadcastQueue(UtaRemoteQueueMessage message)
    {
        foreach (ClientConnection client in clients.Values)
            _ = sendQueueSafelyAsync(client, message);
    }

    private static async Task sendQueueSafelyAsync(ClientConnection client, UtaRemoteQueueMessage message)
    {
        try
        {
            await client.SendJsonAsync(message, CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
        }
    }

    private async Task acceptLoopAsync(HttpListener activeListener, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested && activeListener.IsListening)
            {
                HttpListenerContext context = await activeListener.GetContextAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
                _ = Task.Run(() => handleContextAsync(context, cancellationToken), CancellationToken.None);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (HttpListenerException) when (!activeListener.IsListening || cancellationToken.IsCancellationRequested)
        {
        }
        catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            setStatus($"Remote listener stopped: {exception.GetBaseException().Message}");
        }
    }

    private async Task handleContextAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        try
        {
            IPAddress? remoteAddress = context.Request.RemoteEndPoint?.Address;
            if (remoteAddress == null || !UtaRemoteNetworkPolicy.IsPrivateOrLoopback(remoteAddress))
            {
                await rejectAsync(context, HttpStatusCode.Forbidden, "Private-network clients only.").ConfigureAwait(false);
                return;
            }

            if (!UtaRemoteNetworkPolicy.IsHostAllowed(context.Request.Headers["Host"], allowedHosts, port)
                || !UtaRemoteNetworkPolicy.IsOriginAllowed(context.Request.Headers["Origin"], allowedHosts, port))
            {
                await rejectAsync(context, HttpStatusCode.Forbidden, "Host or Origin rejected.").ConfigureAwait(false);
                return;
            }

            string path = context.Request.Url?.AbsolutePath ?? "/";
            switch (path)
            {
                case "/":
                case "/index.html":
                    await serveRemoteHtmlAsync(context).ConfigureAwait(false);
                    return;

                case "/health":
                    await writeJsonAsync(context, new
                    {
                        status = "ok",
                        protocolVersion = UtaRemoteProtocol.VERSION,
                        clients = clients.Count,
                    }).ConfigureAwait(false);
                    return;

                case "/ws":
                    await handleWebSocketAsync(context, cancellationToken).ConfigureAwait(false);
                    return;

                default:
                    await rejectAsync(context, HttpStatusCode.NotFound, "Not found.").ConfigureAwait(false);
                    return;
            }
        }
        catch (Exception exception)
        {
            try
            {
                await rejectAsync(context, HttpStatusCode.InternalServerError, "Remote request failed.").ConfigureAwait(false);
            }
            catch
            {
            }

            setStatus($"Remote request error: {exception.GetBaseException().Message}");
        }
    }

    private async Task handleWebSocketAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        if (!context.Request.IsWebSocketRequest)
        {
            await rejectAsync(context, HttpStatusCode.BadRequest, "WebSocket upgrade required.").ConfigureAwait(false);
            return;
        }

        if (clients.Count >= maximum_clients)
        {
            await rejectAsync(context, HttpStatusCode.ServiceUnavailable, "Remote client limit reached.").ConfigureAwait(false);
            return;
        }

        UtaRemoteSession? session;
        string? newSecret = null;
        string ticket = context.Request.QueryString["ticket"] ?? string.Empty;
        string sessionId = context.Request.QueryString["session"] ?? string.Empty;
        string sessionSecret = context.Request.QueryString["secret"] ?? string.Empty;

        if (!string.IsNullOrEmpty(ticket))
        {
            if (!credentials.TryRedeem(ticket, DateTimeOffset.UtcNow, out session, out newSecret, out string error))
            {
                await rejectAsync(context, HttpStatusCode.Unauthorized, error).ConfigureAwait(false);
                return;
            }
        }
        else if (!credentials.TryResume(sessionId, sessionSecret, DateTimeOffset.UtcNow, out session))
        {
            await rejectAsync(context, HttpStatusCode.Unauthorized, "Valid pairing or reconnect credentials are required.").ConfigureAwait(false);
            return;
        }

        if (session == null)
        {
            await rejectAsync(context, HttpStatusCode.Unauthorized, "Session allocation failed.").ConfigureAwait(false);
            return;
        }

        HttpListenerWebSocketContext upgraded = await context.AcceptWebSocketAsync(null).ConfigureAwait(false);
        var connection = new ClientConnection(session, upgraded.WebSocket);
        if (clients.TryGetValue(session.Id, out ClientConnection? oldConnection))
            await oldConnection.CloseAsync(WebSocketCloseStatus.NormalClosure, "Reconnected elsewhere", CancellationToken.None).ConfigureAwait(false);
        clients[session.Id] = connection;

        try
        {
            if (newSecret != null)
            {
                await connection.SendJsonAsync(new UtaRemoteWelcome(
                    "welcome",
                    session.Id,
                    newSecret,
                    session.Role,
                    UtaRemoteProtocol.VERSION,
                    snapshotProvider()), cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await connection.SendJsonAsync(new
                {
                    type = "resumed",
                    role = session.Role,
                    protocolVersion = UtaRemoteProtocol.VERSION,
                    snapshot = snapshotProvider(),
                }, cancellationToken).ConfigureAwait(false);
            }

            setStatus($"{session.Role} connected from {context.Request.RemoteEndPoint?.Address}.");
            await receiveLoopAsync(connection, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (clients.TryGetValue(session.Id, out ClientConnection? current) && ReferenceEquals(current, connection))
                clients.TryRemove(session.Id, out _);
            await connection.DisposeAsync().ConfigureAwait(false);
            setStatus(clients.IsEmpty ? "Remote service running; no connected clients." : $"{clients.Count} remote client(s) connected.");
        }
    }

    private async Task receiveLoopAsync(ClientConnection connection, CancellationToken cancellationToken)
    {
        byte[] rented = ArrayPool<byte>.Shared.Rent(8192);
        try
        {
            using var message = new MemoryStream();
            while (!cancellationToken.IsCancellationRequested && connection.Socket.State == WebSocketState.Open)
            {
                WebSocketReceiveResult result = await connection.Socket.ReceiveAsync(new ArraySegment<byte>(rented), cancellationToken).ConfigureAwait(false);
                if (result.MessageType == WebSocketMessageType.Close)
                    break;
                if (result.MessageType != WebSocketMessageType.Text)
                {
                    await connection.SendErrorAsync(0, "Only text JSON messages are supported.", cancellationToken).ConfigureAwait(false);
                    continue;
                }

                if (message.Length + result.Count > UtaRemoteProtocol.MAX_MESSAGE_BYTES)
                {
                    await connection.CloseAsync(WebSocketCloseStatus.MessageTooBig, "Message too large", cancellationToken).ConfigureAwait(false);
                    return;
                }

                message.Write(rented, 0, result.Count);
                if (!result.EndOfMessage)
                    continue;

                byte[] payload = message.ToArray();
                if (!UtaRemoteProtocol.TryParseCommand(payload, connection.Session.Role, out UtaRemoteCommand? command, out string parseError)
                    || command == null)
                {
                    await connection.SendErrorAsync(0, parseError, cancellationToken).ConfigureAwait(false);
                    message.SetLength(0);
                    continue;
                }

                message.SetLength(0);
                if (!connection.Session.CommandLimiter.TryConsume())
                {
                    await connection.SendErrorAsync(command.Sequence, "Command rate limit exceeded.", cancellationToken).ConfigureAwait(false);
                    continue;
                }

                if (!connection.Session.ReplayGuard.TryAdvance(command.Sequence))
                {
                    await connection.SendErrorAsync(command.Sequence, "Sequence was replayed or out of order.", cancellationToken).ConfigureAwait(false);
                    continue;
                }

                if (command.Name == UtaRemoteCommands.SkipCurrent)
                    osu.Framework.Logging.Logger.Log($"Uta remote skip received: sequence={command.Sequence} session={connection.Session.Id}");

                if (command.Name == UtaRemoteCommands.Disconnect)
                {
                    credentials.Revoke(connection.Session.Id);
                    await connection.SendAckAsync(command.Sequence, cancellationToken).ConfigureAwait(false);
                    await connection.CloseAsync(WebSocketCloseStatus.NormalClosure, "Disconnected by client", cancellationToken).ConfigureAwait(false);
                    return;
                }

                UtaRemoteCommandResult outcome = await commandTarget.ExecuteAsync(command, cancellationToken).ConfigureAwait(false);
                if (outcome.Accepted)
                {
                    await connection.SendAckAsync(command.Sequence, cancellationToken).ConfigureAwait(false);
                    if (command.RequestId != null)
                        await connection.SendJsonAsync(new
                        {
                            type = "commandResult",
                            requestId = command.RequestId,
                            accepted = true,
                            library = outcome.LibraryEntries,
                        }, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    await connection.SendErrorAsync(command.Sequence, outcome.Error ?? "Command rejected.", cancellationToken).ConfigureAwait(false);
                    if (command.RequestId != null)
                        await connection.SendJsonAsync(new { type = "commandResult", requestId = command.RequestId, accepted = false, error = outcome.Error }, cancellationToken).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (WebSocketException)
        {
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    private async Task broadcastLoopAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(snapshot_interval_ms));
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                if (clients.IsEmpty)
                    continue;

                UtaRemoteSnapshot snapshot = snapshotProvider();
                byte[] json = JsonSerializer.SerializeToUtf8Bytes(new { type = "state", snapshot }, UtaRemoteProtocol.JsonOptions);
                foreach ((string id, ClientConnection client) in clients.ToArray())
                {
                    if (!client.QueueLatestState(json))
                    {
                        clients.TryRemove(id, out _);
                        await client.DisposeAsync().ConfigureAwait(false);
                    }
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private static async Task serveRemoteHtmlAsync(HttpListenerContext context)
    {
        byte[] html = UtaRemoteAssets.GetHtml();
        HttpListenerResponse response = context.Response;
        response.StatusCode = (int)HttpStatusCode.OK;
        response.ContentType = "text/html; charset=utf-8";
        response.ContentEncoding = Encoding.UTF8;
        response.ContentLength64 = html.Length;
        response.Headers["Cache-Control"] = "no-store, max-age=0";
        response.Headers["Pragma"] = "no-cache";
        response.Headers["X-Content-Type-Options"] = "nosniff";
        response.Headers["Referrer-Policy"] = "no-referrer";
        response.Headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
        response.Headers["Content-Security-Policy"] = "default-src 'none'; style-src 'unsafe-inline'; script-src 'unsafe-inline' 'wasm-unsafe-eval'; connect-src ws: wss:; img-src data:; base-uri 'none'; form-action 'none'; frame-ancestors 'none'";
        await response.OutputStream.WriteAsync(html.AsMemory()).ConfigureAwait(false);
        response.Close();
    }

    private static async Task writeJsonAsync(HttpListenerContext context, object value)
    {
        byte[] body = JsonSerializer.SerializeToUtf8Bytes(value, UtaRemoteProtocol.JsonOptions);
        context.Response.StatusCode = (int)HttpStatusCode.OK;
        context.Response.ContentType = "application/json; charset=utf-8";
        context.Response.ContentLength64 = body.Length;
        context.Response.Headers["Cache-Control"] = "no-store";
        await context.Response.OutputStream.WriteAsync(body.AsMemory()).ConfigureAwait(false);
        context.Response.Close();
    }

    private static async Task rejectAsync(HttpListenerContext context, HttpStatusCode statusCode, string message)
    {
        if (context.Response.OutputStream.CanWrite)
        {
            byte[] body = Encoding.UTF8.GetBytes(message);
            context.Response.StatusCode = (int)statusCode;
            context.Response.ContentType = "text/plain; charset=utf-8";
            context.Response.ContentLength64 = body.Length;
            context.Response.Headers["Cache-Control"] = "no-store";
            await context.Response.OutputStream.WriteAsync(body.AsMemory()).ConfigureAwait(false);
        }

        context.Response.Close();
    }

    private void setStatus(string value)
    {
        lock (statusSync)
            statusMessage = value;
        StatusChanged?.Invoke();
    }

    public async Task StopAsync()
    {
        await lifecycleGate.WaitAsync().ConfigureAwait(false);
        try
        {
            CancellationTokenSource? cancellation = lifetimeCancellation;
            lifetimeCancellation = null;
            cancellation?.Cancel();

            HttpListener? activeListener = listener;
            listener = null;
            if (activeListener != null)
            {
                activeListener.Stop();
                activeListener.Close();
            }

            RevokeAll();
            Task[] tasks = new[] { acceptTask, broadcastTask }.OfType<Task>().ToArray();
            acceptTask = null;
            broadcastTask = null;
            if (tasks.Length > 0)
            {
                try
                {
                    await Task.WhenAll(tasks).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                }
            }

            cancellation?.Dispose();
            listenAddresses = Array.Empty<IPAddress>();
            port = 0;
            setStatus("Remote service stopped.");
        }
        finally
        {
            lifecycleGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        credentials.Dispose();
        lifecycleGate.Dispose();
    }

    private sealed class ClientConnection : IAsyncDisposable
    {
        private const int maximum_pending_messages = 128;

        private readonly Channel<OutboundMessage> outbound = Channel.CreateBounded<OutboundMessage>(new BoundedChannelOptions(maximum_pending_messages)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false,
        });
        private readonly SemaphoreSlim socketGate = new(1, 1);
        private readonly CancellationTokenSource sendCancellation = new();
        private readonly Task sendTask;
        private byte[]? latestState;
        private int stateMessageQueued;

        public UtaRemoteSession Session { get; }
        public WebSocket Socket { get; }

        public ClientConnection(UtaRemoteSession session, WebSocket socket)
        {
            Session = session;
            Socket = socket;
            sendTask = sendLoopAsync();
        }

        public Task SendJsonAsync(object value, CancellationToken cancellationToken)
            => SendBytesAsync(JsonSerializer.SerializeToUtf8Bytes(value, UtaRemoteProtocol.JsonOptions), cancellationToken);

        public Task SendAckAsync(long sequence, CancellationToken cancellationToken)
            => SendJsonAsync(new { type = "ack", sequence }, cancellationToken);

        public Task SendErrorAsync(long sequence, string error, CancellationToken cancellationToken)
            => SendJsonAsync(new { type = "error", sequence, error }, cancellationToken);

        public Task SendBytesAsync(ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken)
        {
            if (Socket.State != WebSocketState.Open)
                return Task.FromException(new WebSocketException("Remote socket is not open."));

            byte[] owned = bytes.ToArray();
            if (outbound.Writer.TryWrite(new OutboundMessage(owned, false)))
                return Task.CompletedTask;

            Socket.Abort();
            return Task.FromException(new WebSocketException("Remote client send queue is full."));
        }

        public bool QueueLatestState(byte[] bytes)
        {
            if (Socket.State != WebSocketState.Open)
                return false;

            Volatile.Write(ref latestState, bytes);
            if (Interlocked.CompareExchange(ref stateMessageQueued, 1, 0) != 0)
                return true;

            if (outbound.Writer.TryWrite(new OutboundMessage(null, true)))
                return true;

            Interlocked.Exchange(ref stateMessageQueued, 0);
            Socket.Abort();
            return false;
        }

        private async Task sendLoopAsync()
        {
            try
            {
                await foreach (OutboundMessage message in outbound.Reader.ReadAllAsync(sendCancellation.Token).ConfigureAwait(false))
                {
                    byte[]? bytes = message.Bytes;
                    if (message.IsState)
                    {
                        Interlocked.Exchange(ref stateMessageQueued, 0);
                        bytes = Volatile.Read(ref latestState);
                    }

                    if (bytes == null)
                        continue;

                    await socketGate.WaitAsync(sendCancellation.Token).ConfigureAwait(false);
                    try
                    {
                        if (Socket.State != WebSocketState.Open)
                            return;
                        await Socket.SendAsync(bytes, WebSocketMessageType.Text, true, sendCancellation.Token).ConfigureAwait(false);
                    }
                    finally
                    {
                        socketGate.Release();
                    }
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (WebSocketException)
            {
            }
        }

        public async Task CloseAsync(WebSocketCloseStatus status, string description, CancellationToken cancellationToken)
        {
            outbound.Writer.TryComplete();
            if (Socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
            {
                await socketGate.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    await Socket.CloseAsync(status, description, cancellationToken).ConfigureAwait(false);
                }
                catch (WebSocketException)
                {
                }
                finally
                {
                    socketGate.Release();
                }
            }
        }

        public async ValueTask DisposeAsync()
        {
            await CloseAsync(WebSocketCloseStatus.NormalClosure, "Session ended", CancellationToken.None).ConfigureAwait(false);
            sendCancellation.Cancel();
            try
            {
                await sendTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            Socket.Dispose();
            sendCancellation.Dispose();
            socketGate.Dispose();
        }

        private readonly record struct OutboundMessage(byte[]? Bytes, bool IsState);
    }
}

internal static class UtaRemoteAssets
{
    private const string resource_suffix = ".Remote.Assets.uta-remote.html";
    private static readonly Lazy<byte[]> html = new(loadHtml, LazyThreadSafetyMode.ExecutionAndPublication);

    public static byte[] GetHtml() => html.Value;

    private static byte[] loadHtml()
    {
        Assembly assembly = typeof(UtaRemoteAssets).Assembly;
        string? resource = assembly.GetManifestResourceNames()
                                   .SingleOrDefault(name => name.EndsWith(resource_suffix, StringComparison.Ordinal));
        if (resource == null)
            throw new InvalidOperationException("Bundled uta remote HTML resource is missing.");

        using Stream stream = assembly.GetManifestResourceStream(resource)
                              ?? throw new InvalidOperationException("Bundled uta remote HTML resource cannot be opened.");
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }
}
