// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENSE file in the repository root for full licence text.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;
using osu.Game.Rulesets.Uta.Queue;

namespace osu.Game.Rulesets.Uta.Remote;

/// <summary>
/// Little-endian framed binary remote protocol. Avoids JSON tokenize/allocate on the
/// 10 Hz snapshot path and on command parse. Frame: U + version + kind + i32 payload + body.
/// </summary>
internal static class UtaRemoteWire
{
    public const byte Magic = (byte)'U';
    public const byte Version = 1;

    public enum Kind : byte
    {
        Command = 1,
        Ack = 2,
        Error = 3,
        Welcome = 4,
        Resumed = 5,
        State = 6,
        Queue = 7,
        Result = 8,
        Trace = 9,
    }

    public static bool IsFrame(ReadOnlySpan<byte> data)
        => data.Length >= 7 && data[0] == Magic && data[1] == Version;

    public static byte IdOf(string name) => name switch
    {
        UtaRemoteCommands.Ping => 1,
        UtaRemoteCommands.Play => 2,
        UtaRemoteCommands.Pause => 3,
        UtaRemoteCommands.TogglePlayback => 4,
        UtaRemoteCommands.Seek => 5,
        UtaRemoteCommands.SeekRelative => 6,
        UtaRemoteCommands.Speed => 7,
        UtaRemoteCommands.SetLoopA => 8,
        UtaRemoteCommands.SetLoopB => 9,
        UtaRemoteCommands.ClearLoop => 10,
        UtaRemoteCommands.PreviousPhrase => 11,
        UtaRemoteCommands.NextPhrase => 12,
        UtaRemoteCommands.RetryPhrase => 13,
        UtaRemoteCommands.LoopPhrase => 14,
        UtaRemoteCommands.BackgroundMusicVolume => 15,
        UtaRemoteCommands.OriginalVocalsVolume => 16,
        UtaRemoteCommands.MicrophoneMonitorVolume => 17,
        UtaRemoteCommands.Transpose => 18,
        UtaRemoteCommands.OctaveFold => 19,
        UtaRemoteCommands.OriginalVocals => 20,
        UtaRemoteCommands.MicrophoneLatency => 21,
        UtaRemoteCommands.AccompanimentLatency => 22,
        UtaRemoteCommands.LyricsLatency => 23,
        UtaRemoteCommands.Disconnect => 24,
        UtaRemoteCommands.LibrarySearch => 25,
        UtaRemoteCommands.QueueAdd => 26,
        UtaRemoteCommands.QueueRemove => 27,
        UtaRemoteCommands.QueueClear => 28,
        UtaRemoteCommands.QueuePlayNow => 29,
        UtaRemoteCommands.SkipCurrent => 30,
        UtaRemoteCommands.SkipToNext => 31,
        UtaRemoteCommands.QueueAddNext => 32,
        UtaRemoteCommands.QueueMove => 33,
        UtaRemoteCommands.QueueMoveToTop => 34,
        UtaRemoteCommands.QueueMoveToBottom => 35,
        UtaRemoteCommands.AutoAdvance => 36,
        UtaRemoteCommands.SetMod => 37,
        UtaRemoteCommands.QueueConfigure => 38,
        _ => 0,
    };

    public static string? NameOf(byte id) => id switch
    {
        1 => UtaRemoteCommands.Ping,
        2 => UtaRemoteCommands.Play,
        3 => UtaRemoteCommands.Pause,
        4 => UtaRemoteCommands.TogglePlayback,
        5 => UtaRemoteCommands.Seek,
        6 => UtaRemoteCommands.SeekRelative,
        7 => UtaRemoteCommands.Speed,
        8 => UtaRemoteCommands.SetLoopA,
        9 => UtaRemoteCommands.SetLoopB,
        10 => UtaRemoteCommands.ClearLoop,
        11 => UtaRemoteCommands.PreviousPhrase,
        12 => UtaRemoteCommands.NextPhrase,
        13 => UtaRemoteCommands.RetryPhrase,
        14 => UtaRemoteCommands.LoopPhrase,
        15 => UtaRemoteCommands.BackgroundMusicVolume,
        16 => UtaRemoteCommands.OriginalVocalsVolume,
        17 => UtaRemoteCommands.MicrophoneMonitorVolume,
        18 => UtaRemoteCommands.Transpose,
        19 => UtaRemoteCommands.OctaveFold,
        20 => UtaRemoteCommands.OriginalVocals,
        21 => UtaRemoteCommands.MicrophoneLatency,
        22 => UtaRemoteCommands.AccompanimentLatency,
        23 => UtaRemoteCommands.LyricsLatency,
        24 => UtaRemoteCommands.Disconnect,
        25 => UtaRemoteCommands.LibrarySearch,
        26 => UtaRemoteCommands.QueueAdd,
        27 => UtaRemoteCommands.QueueRemove,
        28 => UtaRemoteCommands.QueueClear,
        29 => UtaRemoteCommands.QueuePlayNow,
        30 => UtaRemoteCommands.SkipCurrent,
        31 => UtaRemoteCommands.SkipToNext,
        32 => UtaRemoteCommands.QueueAddNext,
        33 => UtaRemoteCommands.QueueMove,
        34 => UtaRemoteCommands.QueueMoveToTop,
        35 => UtaRemoteCommands.QueueMoveToBottom,
        36 => UtaRemoteCommands.AutoAdvance,
        37 => UtaRemoteCommands.SetMod,
        38 => UtaRemoteCommands.QueueConfigure,
        _ => null,
    };

    public static bool TryReadKind(byte[] frame, out Kind kind, out byte[] payload)
    {
        kind = 0;
        payload = Array.Empty<byte>();
        if (!IsFrame(frame))
            return false;
        int length = BinaryPrimitives.ReadInt32LittleEndian(frame.AsSpan(3, 4));
        if (length < 0 || 7 + length > frame.Length)
            return false;
        kind = (Kind)frame[2];
        payload = frame.AsSpan(7, length).ToArray();
        return true;
    }

    public static bool TryParseCommand(byte[] payload, UtaRemoteRole role, out UtaRemoteCommand? command, out string error)
    {
        command = null;
        error = string.Empty;
        var reader = new Reader(payload);
        if (!reader.TryI64(out long sequence) || sequence <= 0)
        {
            error = "A positive integer sequence is required.";
            return false;
        }

        if (!reader.TryU8(out byte id) || NameOf(id) is not string name)
        {
            error = "Unknown command.";
            return false;
        }

        if (role == UtaRemoteRole.Spectator && !UtaRemoteCommands.IsAllowedForSpectator(name))
        {
            error = "Spectator sessions are read-only.";
            return false;
        }

        if (!reader.TryU8(out byte flags))
        {
            error = "Malformed command.";
            return false;
        }

        double? number = null;
        if ((flags & 1) != 0)
        {
            if (!reader.TryF64(out double value) || !double.IsFinite(value))
            {
                error = "The value field must be a finite number.";
                return false;
            }

            number = value;
        }

        bool? enabled = null;
        if ((flags & 2) != 0)
        {
            if (!reader.TryU8(out byte flag))
            {
                error = "The enabled field must be boolean.";
                return false;
            }

            enabled = flag != 0;
        }

        string? text = null;
        if ((flags & 4) != 0 && !reader.TryStr(out text))
        {
            error = "The text field is invalid.";
            return false;
        }

        string? requestId = null;
        if ((flags & 8) != 0 && !reader.TryStr(out requestId))
        {
            error = "The requestId field is invalid.";
            return false;
        }

        UtaQueuePlaybackOptions? options = null;
        if ((flags & 16) != 0)
        {
            if (!reader.TryF64(out double speed) || !reader.TryI8(out sbyte transpose) || !reader.TryU8(out byte modCount))
            {
                error = "The options field is invalid.";
                return false;
            }

            var mods = new string[modCount];
            for (int i = 0; i < modCount; i++)
            {
                if (!reader.TryStr(out string? acronym) || string.IsNullOrWhiteSpace(acronym))
                {
                    error = "A reservation mod acronym is invalid.";
                    return false;
                }

                mods[i] = acronym;
            }

            options = new UtaQueuePlaybackOptions(speed, transpose, mods);
            if (name is UtaRemoteCommands.QueueAdd or UtaRemoteCommands.QueueAddNext or UtaRemoteCommands.QueueConfigure
                && !options.TryValidate(out error))
                return false;
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

        if (number is { } numeric && !numberAllowed(name, numeric))
        {
            error = "The numeric value is outside the desktop control bounds.";
            return false;
        }

        command = new UtaRemoteCommand(sequence, name, number, enabled, text, requestId, role, options);
        return true;
    }

    public static bool TryParseTrace(byte[] payload, out string eventName, out string detail)
    {
        var reader = new Reader(payload);
        if (!reader.TryStr(out string? name) || !reader.TryStr(out string? text))
        {
            eventName = string.Empty;
            detail = string.Empty;
            return false;
        }

        eventName = name ?? string.Empty;
        detail = text ?? string.Empty;
        return true;
    }

    public static byte[] Ack(long sequence) => write(Kind.Ack, writer => writer.I64(sequence));

    public static byte[] Error(long sequence, string error) => write(Kind.Error, writer =>
    {
        writer.I64(sequence);
        writer.Str(error);
    });

    public static byte[] Welcome(UtaRemoteWelcome welcome) => write(Kind.Welcome, writer =>
    {
        writer.Str(welcome.SessionId);
        writer.Str(welcome.SessionSecret);
        writer.U8((byte)(welcome.Role == UtaRemoteRole.Spectator ? 1 : 0));
        writer.I32(welcome.ProtocolVersion);
        writeSnapshot(writer, welcome.Snapshot);
    });

    public static byte[] Resumed(UtaRemoteRole role, UtaRemoteSnapshot snapshot) => write(Kind.Resumed, writer =>
    {
        writer.U8((byte)(role == UtaRemoteRole.Spectator ? 1 : 0));
        writeSnapshot(writer, snapshot);
    });

    public static byte[] State(UtaRemoteSnapshot snapshot) => write(Kind.State, writer => writeSnapshot(writer, snapshot));

    public static byte[] Queue(UtaRemoteQueueMessage queue) => write(Kind.Queue, writer =>
    {
        writer.I64(queue.Revision);
        writer.Bool(queue.AutoAdvanceEnabled);
        writeQueue(writer, queue.Entries);
    });

    public static byte[] Result(string? requestId, bool accepted, string? error, IReadOnlyList<UtaRemoteLibraryEntrySnapshot>? library)
        => write(Kind.Result, writer =>
        {
            writer.Str(requestId);
            writer.Bool(accepted);
            writer.Str(error);
            writeLibrary(writer, library);
        });

    private static void writeSnapshot(Writer writer, UtaRemoteSnapshot snapshot)
    {
        writer.I64(snapshot.Revision);
        writer.F64(snapshot.SongTime);
        writer.F64(snapshot.SongLength);
        writer.Bool(snapshot.Paused);
        writer.F64(snapshot.Speed);
        writer.I32(snapshot.PhraseIndex);
        writer.I32(snapshot.PhraseCount);
        writer.F64(snapshot.Score);
        writer.F64(snapshot.PitchSimilarity);
        writer.Bool(snapshot.VoiceActive);
        writer.F64(snapshot.DetectedPitchMidi ?? double.NaN);
        writer.F64(snapshot.Loop.A ?? double.NaN);
        writer.F64(snapshot.Loop.B ?? double.NaN);
        writer.Bool(snapshot.Loop.CurrentPhrase);
        writer.F64(snapshot.Mixer.BackgroundMusic);
        writer.F64(snapshot.Mixer.OriginalVocals);
        writer.F64(snapshot.Mixer.MicrophoneMonitor);
        writer.I32(snapshot.Mixer.Transpose);
        writer.Bool(snapshot.Mixer.OctaveFold);
        writer.Bool(snapshot.Mixer.OriginalVocalsEnabled);
        writer.F64(snapshot.Mixer.MicrophoneLatency);
        writer.F64(snapshot.Mixer.AccompanimentLatency);
        writer.F64(snapshot.Mixer.LyricsLatency);
        writer.Bool(snapshot.AutoAdvanceEnabled);
        writer.I64(snapshot.QueueRevision);
        writer.Str(snapshot.Notice);
        writer.Str(snapshot.CurrentLyrics);
        writer.Str(snapshot.NextLyrics);
        writer.Str(snapshot.SongTitle);
        writer.Str(snapshot.SongArtist);
        writer.Str(snapshot.SongDifficulty);
        writer.Str(snapshot.SongCreator);
        writeQueue(writer, snapshot.Queue);
        IReadOnlyList<UtaRemoteModSnapshot> mods = snapshot.Mods ?? Array.Empty<UtaRemoteModSnapshot>();
        writer.U16((ushort)Math.Min(mods.Count, ushort.MaxValue));
        int count = Math.Min(mods.Count, ushort.MaxValue);
        for (int i = 0; i < count; i++)
        {
            writer.Str(mods[i].Acronym);
            writer.Str(mods[i].Name);
            writer.Bool(mods[i].Enabled);
        }
    }

    private static void writeQueue(Writer writer, IReadOnlyList<UtaRemoteQueueEntrySnapshot> entries)
    {
        int count = Math.Min(entries.Count, ushort.MaxValue);
        writer.U16((ushort)count);
        for (int i = 0; i < count; i++)
        {
            UtaRemoteQueueEntrySnapshot entry = entries[i];
            writer.Str(entry.Id);
            writer.Str(entry.Title);
            writer.Str(entry.Artist);
            writer.Str(entry.DifficultyName);
            writer.F64(entry.LengthMs);
            writer.F64(entry.Speed);
            writer.I32(entry.Transpose);
            IReadOnlyList<string> mods = entry.Mods ?? Array.Empty<string>();
            writer.U8((byte)Math.Min(mods.Count, 32));
            int modCount = Math.Min(mods.Count, 32);
            for (int m = 0; m < modCount; m++)
                writer.Str(mods[m]);
        }
    }

    private static void writeLibrary(Writer writer, IReadOnlyList<UtaRemoteLibraryEntrySnapshot>? songs)
    {
        int count = songs == null ? 0 : Math.Min(songs.Count, ushort.MaxValue);
        writer.U16((ushort)count);
        for (int i = 0; i < count; i++)
        {
            UtaRemoteLibraryEntrySnapshot song = songs![i];
            writer.Str(song.BeatmapId);
            writer.Str(song.Title);
            writer.Str(song.Artist);
            writer.Str(song.DifficultyName);
            writer.Str(song.Creator);
            writer.F64(song.LengthMs);
        }
    }

    private static byte[] write(Kind kind, Action<Writer> writePayload)
    {
        var writer = new Writer();
        writePayload(writer);
        byte[] payload = writer.ToArray();
        byte[] frame = new byte[7 + payload.Length];
        frame[0] = Magic;
        frame[1] = Version;
        frame[2] = (byte)kind;
        BinaryPrimitives.WriteInt32LittleEndian(frame.AsSpan(3, 4), payload.Length);
        payload.CopyTo(frame.AsSpan(7));
        return frame;
    }

    private static bool numberAllowed(string command, double value)
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

    private ref struct Reader
    {
        private ReadOnlySpan<byte> data;

        public Reader(ReadOnlySpan<byte> data) => this.data = data;

        public bool TryU8(out byte value)
        {
            if (data.Length < 1)
            {
                value = 0;
                return false;
            }

            value = data[0];
            data = data[1..];
            return true;
        }

        public bool TryI8(out sbyte value)
        {
            if (!TryU8(out byte raw))
            {
                value = 0;
                return false;
            }

            value = unchecked((sbyte)raw);
            return true;
        }

        public bool TryI32(out int value)
        {
            if (data.Length < 4)
            {
                value = 0;
                return false;
            }

            value = BinaryPrimitives.ReadInt32LittleEndian(data);
            data = data[4..];
            return true;
        }

        public bool TryI64(out long value)
        {
            if (data.Length < 8)
            {
                value = 0;
                return false;
            }

            value = BinaryPrimitives.ReadInt64LittleEndian(data);
            data = data[8..];
            return true;
        }

        public bool TryF64(out double value)
        {
            if (data.Length < 8)
            {
                value = 0;
                return false;
            }

            value = BinaryPrimitives.ReadDoubleLittleEndian(data);
            data = data[8..];
            return true;
        }

        public bool TryStr(out string? value)
        {
            if (data.Length < 2)
            {
                value = null;
                return false;
            }

            int length = BinaryPrimitives.ReadUInt16LittleEndian(data);
            data = data[2..];
            if (data.Length < length)
            {
                value = null;
                return false;
            }

            value = length == 0 ? string.Empty : Encoding.UTF8.GetString(data[..length]);
            data = data[length..];
            return true;
        }
    }

    private sealed class Writer
    {
        private readonly MemoryStream stream = new();

        public void U8(byte value) => stream.WriteByte(value);
        public void U16(ushort value)
        {
            Span<byte> bytes = stackalloc byte[2];
            BinaryPrimitives.WriteUInt16LittleEndian(bytes, value);
            stream.Write(bytes);
        }

        public void I32(int value)
        {
            Span<byte> bytes = stackalloc byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(bytes, value);
            stream.Write(bytes);
        }

        public void I64(long value)
        {
            Span<byte> bytes = stackalloc byte[8];
            BinaryPrimitives.WriteInt64LittleEndian(bytes, value);
            stream.Write(bytes);
        }

        public void F64(double value)
        {
            Span<byte> bytes = stackalloc byte[8];
            BinaryPrimitives.WriteDoubleLittleEndian(bytes, value);
            stream.Write(bytes);
        }

        public void Bool(bool value) => stream.WriteByte(value ? (byte)1 : (byte)0);

        public void Str(string? value)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(value ?? string.Empty);
            int length = Math.Min(bytes.Length, ushort.MaxValue);
            U16((ushort)length);
            stream.Write(bytes, 0, length);
        }

        public byte[] ToArray() => stream.ToArray();
    }
}
