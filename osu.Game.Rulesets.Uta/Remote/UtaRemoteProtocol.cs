// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENSE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace osu.Game.Rulesets.Uta.Remote;

public enum UtaRemoteRole
{
    Controller,
    Spectator,
}

public static class UtaRemoteCommands
{
    public const string Ping = "ping";
    public const string Play = "play";
    public const string Pause = "pause";
    public const string TogglePlayback = "togglePlayback";
    public const string Seek = "seek";
    public const string SeekRelative = "seekRelative";
    public const string Speed = "speed";
    public const string SetLoopA = "setLoopA";
    public const string SetLoopB = "setLoopB";
    public const string ClearLoop = "clearLoop";
    public const string PreviousPhrase = "previousPhrase";
    public const string NextPhrase = "nextPhrase";
    public const string RetryPhrase = "retryPhrase";
    public const string LoopPhrase = "loopPhrase";
    public const string BackgroundMusicVolume = "bgmVolume";
    public const string OriginalVocalsVolume = "vocalsVolume";
    public const string MicrophoneMonitorVolume = "monitorVolume";
    public const string Transpose = "transpose";
    public const string OctaveFold = "octaveFold";
    public const string OriginalVocals = "originalVocals";
    public const string MicrophoneLatency = "microphoneLatency";
    public const string AccompanimentLatency = "accompanimentLatency";
    public const string LyricsLatency = "lyricsLatency";
    public const string Disconnect = "disconnect";
    public const string LibrarySearch = "librarySearch";
    public const string QueueAdd = "queueAdd";
    public const string QueueRemove = "queueRemove";
    public const string QueueClear = "queueClear";
    public const string QueuePlayNow = "queuePlayNow";
    public const string SkipCurrent = "skipCurrent";
    public const string SkipToNext = "skipToNext";
    public const string QueueAddNext = "queueAddNext";
    public const string QueueMove = "queueMove";
    public const string QueueMoveToTop = "queueMoveToTop";
    public const string QueueMoveToBottom = "queueMoveToBottom";
    public const string AutoAdvance = "autoAdvance";
    public const string SetMod = "setMod";
    public const string QueueConfigure = "queueConfigure";

    /// <summary>
    /// Commands a read-only Spectator session is still permitted to send. Song requests are
    /// deliberately allowed for guests who otherwise cannot touch playback controls.
    /// </summary>
    private static readonly HashSet<string> spectatorAllowed = new(StringComparer.Ordinal)
    {
        Ping,
        Disconnect,
        LibrarySearch,
        QueueAdd,
    };

    private static readonly HashSet<string> all = new(StringComparer.Ordinal)
    {
        Ping,
        Play,
        Pause,
        TogglePlayback,
        Seek,
        SeekRelative,
        Speed,
        SetLoopA,
        SetLoopB,
        ClearLoop,
        PreviousPhrase,
        NextPhrase,
        RetryPhrase,
        LoopPhrase,
        BackgroundMusicVolume,
        OriginalVocalsVolume,
        MicrophoneMonitorVolume,
        Transpose,
        OctaveFold,
        OriginalVocals,
        MicrophoneLatency,
        AccompanimentLatency,
        LyricsLatency,
        Disconnect,
        LibrarySearch,
        QueueAdd,
        QueueRemove,
        QueueClear,
        QueuePlayNow,
        SkipCurrent,
        SkipToNext,
        QueueAddNext,
        QueueMove,
        QueueMoveToTop,
        QueueMoveToBottom,
        AutoAdvance,
        SetMod,
        QueueConfigure,
    };

    internal static bool IsKnown(string command) => all.Contains(command);

    internal static bool IsAllowedForSpectator(string command) => spectatorAllowed.Contains(command);

    internal static bool RequiresNumber(string command)
        => command is Seek or SeekRelative or Speed
            or QueueMove
            or BackgroundMusicVolume or OriginalVocalsVolume or MicrophoneMonitorVolume
            or Transpose or MicrophoneLatency or AccompanimentLatency or LyricsLatency;

    internal static bool RequiresBoolean(string command)
        => command is LoopPhrase or OctaveFold or OriginalVocals or AutoAdvance or SetMod;
}

public sealed record UtaRemoteCommand(
    long Sequence,
    string Name,
    double? Number,
    bool? Enabled,
    string? Text,
    string? RequestId = null,
    UtaRemoteRole Role = UtaRemoteRole.Controller,
    Queue.UtaQueuePlaybackOptions? Options = null);

public sealed record UtaRemoteLibraryEntrySnapshot(
    string BeatmapId,
    string Title,
    string Artist,
    string DifficultyName,
    string Creator,
    long LengthMs);

public sealed record UtaRemoteCommandResult(
    bool Accepted,
    string? Error = null,
    IReadOnlyList<UtaRemoteLibraryEntrySnapshot>? LibraryEntries = null)
{
    public static UtaRemoteCommandResult Ok() => new(true);

    public static UtaRemoteCommandResult Reject(string error) => new(false, error);
}

public interface IUtaRemoteCommandTarget
{
    ValueTask<UtaRemoteCommandResult> ExecuteAsync(UtaRemoteCommand command, CancellationToken cancellationToken);
}

public sealed record UtaRemoteLoopSnapshot(double? A, double? B, bool CurrentPhrase);

public sealed record UtaRemoteMixerSnapshot(
    double BackgroundMusic,
    double OriginalVocals,
    double MicrophoneMonitor,
    int Transpose,
    bool OctaveFold,
    bool OriginalVocalsEnabled,
    double MicrophoneLatency,
    double AccompanimentLatency,
    double LyricsLatency);

public sealed record UtaRemoteQueueEntrySnapshot(
    string Id,
    string Title,
    string Artist,
    DateTimeOffset RequestedAt,
    string? DifficultyName = null,
    long LengthMs = 0,
    double Speed = 1,
    int Transpose = 0,
    IReadOnlyList<string>? Mods = null);

public sealed record UtaRemoteQueueMessage(
    string Type,
    long Revision,
    bool AutoAdvanceEnabled,
    IReadOnlyList<UtaRemoteQueueEntrySnapshot> Entries);

public sealed record UtaRemoteModSnapshot(string Acronym, string Name, bool Enabled);

public sealed record UtaRemoteSnapshot(
    long Revision,
    double SongTime,
    double SongLength,
    bool Paused,
    double Speed,
    int PhraseIndex,
    int PhraseCount,
    string CurrentLyrics,
    string? NextLyrics,
    double? DetectedPitchMidi,
    double PitchSimilarity,
    bool VoiceActive,
    double Score,
    UtaRemoteLoopSnapshot Loop,
    UtaRemoteMixerSnapshot Mixer,
    IReadOnlyList<UtaRemoteQueueEntrySnapshot> Queue,
    bool AutoAdvanceEnabled,
    string? Notice = null,
    long QueueRevision = 0,
    IReadOnlyList<UtaRemoteModSnapshot>? Mods = null,
    string? SongTitle = null,
    string? SongArtist = null,
    string? SongDifficulty = null,
    string? SongCreator = null)
{
    public static UtaRemoteSnapshot Empty(string? notice = null) => new(
        0, 0, 0, true, 1, -1, 0, string.Empty, null, null, 0, false, 0,
        new UtaRemoteLoopSnapshot(null, null, false),
        new UtaRemoteMixerSnapshot(1, 0.55, 0.35, 0, false, false, 0, 0, 0),
        Array.Empty<UtaRemoteQueueEntrySnapshot>(), false, notice);
}

public sealed record UtaRemoteWelcome(
    string Type,
    string SessionId,
    string SessionSecret,
    UtaRemoteRole Role,
    int ProtocolVersion,
    UtaRemoteSnapshot Snapshot);

public static class UtaRemoteProtocol
{
    public const int VERSION = 1;
    public const int MAX_MESSAGE_BYTES = 32 * 1024;
    public const int MAX_JSON_DEPTH = 12;

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DictionaryKeyPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        NumberHandling = JsonNumberHandling.Strict,
        MaxDepth = MAX_JSON_DEPTH,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static bool TryParseCommand(
        ReadOnlyMemory<byte> utf8,
        UtaRemoteRole role,
        out UtaRemoteCommand? command,
        out string error)
    {
        command = null;
        error = string.Empty;

        if (utf8.Length == 0)
        {
            error = "Empty command.";
            return false;
        }

        if (utf8.Length > MAX_MESSAGE_BYTES)
        {
            error = "Command exceeds the message-size limit.";
            return false;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(utf8, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = MAX_JSON_DEPTH,
            });

            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                error = "Command must be a JSON object.";
                return false;
            }

            string type = readRequiredString(root, "type", 16);
            if (!string.Equals(type, "command", StringComparison.Ordinal))
            {
                error = "Unsupported message type.";
                return false;
            }

            if (!root.TryGetProperty("sequence", out JsonElement sequenceElement)
                || !sequenceElement.TryGetInt64(out long sequence)
                || sequence <= 0)
            {
                error = "A positive integer sequence is required.";
                return false;
            }

            string name = root.TryGetProperty("name", out _)
                ? readRequiredString(root, "name", 64)
                : readRequiredString(root, "command", 64);
            if (!UtaRemoteCommands.IsKnown(name))
            {
                error = "Unknown command.";
                return false;
            }

            if (role == UtaRemoteRole.Spectator && !UtaRemoteCommands.IsAllowedForSpectator(name))
            {
                error = "Spectator sessions are read-only.";
                return false;
            }

            double? number = null;
            if (root.TryGetProperty("value", out JsonElement numberElement)
                && numberElement.ValueKind != JsonValueKind.Null)
            {
                if (!numberElement.TryGetDouble(out double parsed) || !double.IsFinite(parsed))
                {
                    error = "The value field must be a finite number.";
                    return false;
                }

                number = parsed;
            }

            bool? enabled = null;
            if (root.TryGetProperty("enabled", out JsonElement enabledElement)
                && enabledElement.ValueKind != JsonValueKind.Null)
            {
                if (enabledElement.ValueKind is not JsonValueKind.True and not JsonValueKind.False)
                {
                    error = "The enabled field must be boolean.";
                    return false;
                }

                enabled = enabledElement.GetBoolean();
            }

            string? text = null;
            if (root.TryGetProperty("text", out JsonElement textElement)
                && textElement.ValueKind != JsonValueKind.Null)
            {
                if (textElement.ValueKind != JsonValueKind.String)
                {
                    error = "The text field must be a string.";
                    return false;
                }

                text = textElement.GetString();
                if (text is { Length: > 256 })
                {
                    error = "The text field is too long.";
                    return false;
                }
            }

            string? requestId = null;
            if (root.TryGetProperty("requestId", out JsonElement requestIdElement)
                && requestIdElement.ValueKind != JsonValueKind.Null)
            {
                if (requestIdElement.ValueKind != JsonValueKind.String)
                {
                    error = "The requestId field must be a string.";
                    return false;
                }

                requestId = requestIdElement.GetString();
                if (string.IsNullOrWhiteSpace(requestId) || requestId.Length > 128)
                {
                    error = "The requestId field is invalid.";
                    return false;
                }
            }

            if (UtaRemoteCommands.RequiresNumber(name) && number == null)
            {
                error = "This command requires a numeric value.";
                return false;
            }

            if (UtaRemoteCommands.RequiresBoolean(name) && enabled == null)
            {
                error = "This command requires an enabled flag.";
                return false;
            }

            if (number is { } numeric && !isNumberAllowed(name, numeric))
            {
                error = "The numeric value is outside the desktop control bounds.";
                return false;
            }

            if (!tryReadOptions(root, out Queue.UtaQueuePlaybackOptions? options, out error))
                return false;

            if (name is UtaRemoteCommands.QueueAdd or UtaRemoteCommands.QueueAddNext or UtaRemoteCommands.QueueConfigure
                && options != null && !options.TryValidate(out error))
                return false;

            command = new UtaRemoteCommand(sequence, name, number, enabled, text, requestId, role, options);
            return true;
        }
        catch (JsonException)
        {
            error = "Malformed JSON command.";
            return false;
        }
        catch (InvalidOperationException exception)
        {
            error = exception.Message;
            return false;
        }
    }

    private static bool isNumberAllowed(string command, double value)
        => command switch
        {
            UtaRemoteCommands.Seek => value is >= 0 and <= 86_400_000,
            UtaRemoteCommands.SeekRelative => value is >= -3_600_000 and <= 3_600_000,
            UtaRemoteCommands.Speed => value is >= 0.5 and <= 1.5,
            UtaRemoteCommands.BackgroundMusicVolume or UtaRemoteCommands.OriginalVocalsVolume
                or UtaRemoteCommands.MicrophoneMonitorVolume => value is >= 0 and <= 1,
            UtaRemoteCommands.Transpose => value is >= -6 and <= 6,
            UtaRemoteCommands.MicrophoneLatency or UtaRemoteCommands.AccompanimentLatency
                or UtaRemoteCommands.LyricsLatency => value is >= -500 and <= 1000,
            _ => true,
        };

    private static bool tryReadOptions(JsonElement root, out Queue.UtaQueuePlaybackOptions? options, out string error)
    {
        options = null;
        error = string.Empty;

        if (!root.TryGetProperty("options", out JsonElement optionsElement) || optionsElement.ValueKind == JsonValueKind.Null)
            return true;

        if (optionsElement.ValueKind != JsonValueKind.Object)
        {
            error = "The options field must be an object.";
            return false;
        }

        double speed = 1;
        if (optionsElement.TryGetProperty("speed", out JsonElement speedElement) && speedElement.ValueKind != JsonValueKind.Null)
        {
            if (!speedElement.TryGetDouble(out speed) || !double.IsFinite(speed))
            {
                error = "The options.speed field must be a finite number.";
                return false;
            }
        }

        int transpose = 0;
        if (optionsElement.TryGetProperty("transpose", out JsonElement transposeElement) && transposeElement.ValueKind != JsonValueKind.Null)
        {
            if (transposeElement.TryGetInt32(out int parsedTranspose))
                transpose = parsedTranspose;
            else if (transposeElement.TryGetDouble(out double transposeNumber)
                     && double.IsFinite(transposeNumber)
                     && Math.Abs(transposeNumber - Math.Round(transposeNumber)) < 0.0001)
                transpose = (int)Math.Round(transposeNumber);
            else
            {
                error = "The options.transpose field must be an integer.";
                return false;
            }
        }

        List<string> mods = new();
        if (optionsElement.TryGetProperty("mods", out JsonElement modsElement) && modsElement.ValueKind != JsonValueKind.Null)
        {
            if (modsElement.ValueKind != JsonValueKind.Array)
            {
                error = "The options.mods field must be an array.";
                return false;
            }

            foreach (JsonElement item in modsElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.String)
                {
                    error = "Each reservation mod must be a string.";
                    return false;
                }

                string? acronym = item.GetString();
                if (string.IsNullOrWhiteSpace(acronym) || acronym.Length > 16)
                {
                    error = "A reservation mod acronym is invalid.";
                    return false;
                }

                mods.Add(acronym);
            }
        }

        options = new Queue.UtaQueuePlaybackOptions(speed, transpose, mods);
        return true;
    }

    private static string readRequiredString(JsonElement root, string name, int maximumLength)
    {
        if (!root.TryGetProperty(name, out JsonElement element) || element.ValueKind != JsonValueKind.String)
            throw new InvalidOperationException($"The {name} field is required.");

        string? value = element.GetString();
        if (string.IsNullOrWhiteSpace(value) || value.Length > maximumLength)
            throw new InvalidOperationException($"The {name} field is invalid.");

        return value;
    }
}
