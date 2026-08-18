// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENSE file in the repository root for full licence text.

using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace osu.Game.Rulesets.Uta.Remote;

public sealed record UtaRemotePairingTicket(string Token, UtaRemoteRole Role, DateTimeOffset ExpiresAt);

public sealed class UtaRemoteSession
{
    internal readonly byte[] SecretHash;
    internal readonly UtaRemoteReplayGuard ReplayGuard = new();
    internal readonly UtaRemoteTokenBucket CommandLimiter = new(20, 40);

    public string Id { get; }
    public UtaRemoteRole Role { get; }
    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset LastSeenAt { get; internal set; }

    internal UtaRemoteSession(string id, UtaRemoteRole role, byte[] secretHash, DateTimeOffset createdAt)
    {
        Id = id;
        Role = role;
        SecretHash = secretHash;
        CreatedAt = createdAt;
        LastSeenAt = createdAt;
    }
}

/// <summary>
/// In-memory, gameplay-session-scoped pairing and reconnect credentials.
/// Pairing tickets are short-lived and single-use. Reconnect secrets never touch disk.
/// </summary>
public sealed class UtaRemoteCredentialStore : IDisposable
{
    public static readonly TimeSpan DEFAULT_PAIRING_LIFETIME = TimeSpan.FromSeconds(90);

    private readonly ConcurrentDictionary<string, PendingTicket> pendingTickets = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, UtaRemoteSession> sessions = new(StringComparer.Ordinal);
    private int disposed;

    public int ActiveSessionCount => sessions.Count;

    public UtaRemotePairingTicket IssuePairingTicket(
        UtaRemoteRole role,
        DateTimeOffset? now = null,
        TimeSpan? lifetime = null)
    {
        throwIfDisposed();
        DateTimeOffset issuedAt = now ?? DateTimeOffset.UtcNow;
        TimeSpan effectiveLifetime = lifetime ?? DEFAULT_PAIRING_LIFETIME;
        if (effectiveLifetime <= TimeSpan.Zero || effectiveLifetime > TimeSpan.FromMinutes(5))
            throw new ArgumentOutOfRangeException(nameof(lifetime));

        purgeExpired(issuedAt);

        string token = createToken(32);
        string key = hashToKey(token);
        DateTimeOffset expiresAt = issuedAt.Add(effectiveLifetime);
        if (!pendingTickets.TryAdd(key, new PendingTicket(role, expiresAt)))
            throw new CryptographicException("A pairing-token collision occurred.");

        return new UtaRemotePairingTicket(token, role, expiresAt);
    }

    public bool TryRedeem(
        string ticket,
        DateTimeOffset? now,
        out UtaRemoteSession? session,
        out string? sessionSecret,
        out string error)
    {
        session = null;
        sessionSecret = null;
        error = string.Empty;
        if (Volatile.Read(ref disposed) != 0)
        {
            error = "Remote service is stopping.";
            return false;
        }

        if (!isCredentialShapeValid(ticket))
        {
            error = "Pairing ticket is invalid.";
            return false;
        }

        DateTimeOffset timestamp = now ?? DateTimeOffset.UtcNow;
        purgeExpired(timestamp);
        string key = hashToKey(ticket);
        if (!pendingTickets.TryRemove(key, out PendingTicket? pending))
        {
            error = "Pairing ticket is unknown, expired, or already used.";
            return false;
        }

        if (pending.ExpiresAt < timestamp)
        {
            error = "Pairing ticket has expired.";
            return false;
        }

        // There is at most one write-capable controller. Pairing a new controller is an
        // explicit desktop action and therefore revokes the previous controller session.
        if (pending.Role == UtaRemoteRole.Controller)
        {
            foreach (UtaRemoteSession existing in sessions.Values.Where(value => value.Role == UtaRemoteRole.Controller))
                sessions.TryRemove(existing.Id, out _);
        }

        string id = createToken(18);
        string secret = createToken(32);
        var created = new UtaRemoteSession(id, pending.Role, SHA256.HashData(Encoding.UTF8.GetBytes(secret)), timestamp);
        if (!sessions.TryAdd(id, created))
        {
            error = "Unable to allocate a remote session.";
            return false;
        }

        session = created;
        sessionSecret = secret;
        return true;
    }

    public bool TryResume(
        string sessionId,
        string sessionSecret,
        DateTimeOffset? now,
        out UtaRemoteSession? session)
    {
        session = null;
        if (Volatile.Read(ref disposed) != 0
            || !isCredentialShapeValid(sessionId)
            || !isCredentialShapeValid(sessionSecret)
            || !sessions.TryGetValue(sessionId, out UtaRemoteSession? candidate))
            return false;

        byte[] suppliedHash = SHA256.HashData(Encoding.UTF8.GetBytes(sessionSecret));
        bool matches = CryptographicOperations.FixedTimeEquals(suppliedHash, candidate.SecretHash);
        CryptographicOperations.ZeroMemory(suppliedHash);
        if (!matches)
            return false;

        candidate.LastSeenAt = now ?? DateTimeOffset.UtcNow;
        session = candidate;
        return true;
    }

    public bool Revoke(string sessionId)
        => sessions.TryRemove(sessionId, out _);

    public int RevokeAll()
    {
        int count = 0;
        foreach (string id in sessions.Keys)
        {
            if (sessions.TryRemove(id, out _))
                count++;
        }

        pendingTickets.Clear();
        return count;
    }

    public IReadOnlyList<UtaRemoteSession> SnapshotSessions()
        => sessions.Values.OrderBy(value => value.CreatedAt).ToArray();

    private void purgeExpired(DateTimeOffset now)
    {
        foreach ((string key, PendingTicket value) in pendingTickets)
        {
            if (value.ExpiresAt < now)
                pendingTickets.TryRemove(key, out _);
        }
    }

    private static string createToken(int bytes)
    {
        byte[] buffer = RandomNumberGenerator.GetBytes(bytes);
        try
        {
            return Convert.ToBase64String(buffer)
                          .TrimEnd('=')
                          .Replace('+', '-')
                          .Replace('/', '_');
        }
        finally
        {
            CryptographicOperations.ZeroMemory(buffer);
        }
    }

    private static string hashToKey(string credential)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(credential)));

    private static bool isCredentialShapeValid(string value)
    {
        if (value.Length is < 20 or > 96)
            return false;

        foreach (char character in value)
        {
            if (!(char.IsAsciiLetterOrDigit(character) || character is '-' or '_'))
                return false;
        }

        return true;
    }

    private void throwIfDisposed()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != 0, this);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
            return;

        RevokeAll();
    }

    private sealed record PendingTicket(UtaRemoteRole Role, DateTimeOffset ExpiresAt);
}

public sealed class UtaRemoteReplayGuard
{
    private long lastSequence;

    public long LastSequence => Interlocked.Read(ref lastSequence);

    public bool TryAdvance(long sequence)
    {
        if (sequence <= 0)
            return false;

        while (true)
        {
            long observed = Interlocked.Read(ref lastSequence);
            if (sequence <= observed)
                return false;

            if (Interlocked.CompareExchange(ref lastSequence, sequence, observed) == observed)
                return true;
        }
    }
}

/// <summary>Thread-safe token bucket using Stopwatch's monotonic clock.</summary>
public sealed class UtaRemoteTokenBucket
{
    private readonly double refillPerSecond;
    private readonly double capacity;
    private readonly object sync = new();
    private double tokens;
    private long lastTimestamp;

    public UtaRemoteTokenBucket(double refillPerSecond, double capacity)
    {
        if (!double.IsFinite(refillPerSecond) || refillPerSecond <= 0)
            throw new ArgumentOutOfRangeException(nameof(refillPerSecond));
        if (!double.IsFinite(capacity) || capacity < 1)
            throw new ArgumentOutOfRangeException(nameof(capacity));

        this.refillPerSecond = refillPerSecond;
        this.capacity = capacity;
        tokens = capacity;
        lastTimestamp = Stopwatch.GetTimestamp();
    }

    public bool TryConsume(double amount = 1)
    {
        if (!double.IsFinite(amount) || amount <= 0 || amount > capacity)
            return false;

        lock (sync)
        {
            long now = Stopwatch.GetTimestamp();
            double elapsed = Stopwatch.GetElapsedTime(lastTimestamp, now).TotalSeconds;
            lastTimestamp = now;
            tokens = Math.Min(capacity, tokens + elapsed * refillPerSecond);
            if (tokens < amount)
                return false;

            tokens -= amount;
            return true;
        }
    }
}

public static class UtaRemoteNetworkPolicy
{
    public const int DEFAULT_PORT = 27835;

    public static IReadOnlyList<IPAddress> GetPrivateListenAddresses()
    {
        var addresses = new HashSet<IPAddress> { IPAddress.Loopback };
        foreach (NetworkInterface network in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (network.OperationalStatus != OperationalStatus.Up
                || network.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
                continue;

            foreach (UnicastIPAddressInformation unicast in network.GetIPProperties().UnicastAddresses)
            {
                IPAddress address = unicast.Address;
                if (address.AddressFamily == AddressFamily.InterNetwork && IsPrivateOrLoopback(address))
                    addresses.Add(address);
            }
        }

        return addresses.OrderBy(address => IPAddress.IsLoopback(address) ? 0 : 1)
                        .ThenBy(address => address.ToString(), StringComparer.Ordinal)
                        .ToArray();
    }

    public static bool IsPrivateOrLoopback(IPAddress address)
    {
        if (IPAddress.IsLoopback(address))
            return true;
        if (address.AddressFamily != AddressFamily.InterNetwork)
            return false;

        byte[] bytes = address.GetAddressBytes();
        return bytes[0] == 10
               || bytes[0] == 127
               || bytes[0] == 192 && bytes[1] == 168
               || bytes[0] == 172 && bytes[1] is >= 16 and <= 31
               || bytes[0] == 169 && bytes[1] == 254;
    }

    public static bool IsHostAllowed(string? hostHeader, IReadOnlySet<string> allowedHosts, int expectedPort)
    {
        if (string.IsNullOrWhiteSpace(hostHeader))
            return false;

        try
        {
            var host = new Uri($"http://{hostHeader}");
            return allowedHosts.Contains(host.Host) && host.Port == expectedPort;
        }
        catch (UriFormatException)
        {
            return false;
        }
    }

    public static bool IsOriginAllowed(string? originHeader, IReadOnlySet<string> allowedHosts, int expectedPort)
    {
        // Non-browser clients may omit Origin. They still require a one-time ticket or
        // reconnect secret, and the Host header is checked independently. Browser clients
        // must originate from this exact HTTP listener, not merely another port on the host.
        if (string.IsNullOrWhiteSpace(originHeader))
            return true;

        return Uri.TryCreate(originHeader, UriKind.Absolute, out Uri? origin)
               && origin.Scheme == Uri.UriSchemeHttp
               && allowedHosts.Contains(origin.Host)
               && origin.Port == expectedPort;
    }

    public static string ToHttpUrl(IPAddress address, int port)
        => $"http://{address}:{port}/";
}
