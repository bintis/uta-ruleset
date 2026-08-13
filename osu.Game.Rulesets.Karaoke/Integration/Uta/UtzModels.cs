// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Generic;
using Newtonsoft.Json;

namespace osu.Game.Rulesets.Karaoke.Integration.Uta;

public sealed class UtzManifest
{
    [JsonProperty("format", Required = Required.Always)]
    public string Format { get; init; } = string.Empty;

    [JsonProperty("format_version", Required = Required.Always)]
    public string FormatVersion { get; init; } = string.Empty;

    [JsonProperty("package_id", Required = Required.Always)]
    public string PackageId { get; init; } = string.Empty;

    [JsonProperty("revision", Required = Required.Always)]
    public int Revision { get; init; }

    [JsonProperty("song", Required = Required.Always)]
    public UtzSongMetadata Song { get; init; } = new();

    [JsonProperty("audio", Required = Required.Always)]
    public UtzAudioAssets Audio { get; init; } = new();

    [JsonProperty("charts", Required = Required.Always)]
    public UtzChartAssets Charts { get; init; } = new();

    [JsonProperty("visuals")]
    public UtzVisualAssets Visuals { get; init; } = new();

    [JsonProperty("scoring", Required = Required.Always)]
    public UtzScoringConfig Scoring { get; init; } = new();

    [JsonProperty("provenance")]
    public UtzProvenance Provenance { get; init; } = new();

    [JsonIgnore]
    public IEnumerable<UtzAsset> Assets
    {
        get
        {
            yield return Audio.Instrumental;
            yield return Charts.Transcript;
            yield return Charts.PitchTrack;
            yield return Charts.PitchNotes;

            if (Audio.GuideVocals != null)
                yield return Audio.GuideVocals;
            if (Audio.Original != null)
                yield return Audio.Original;
            if (Visuals.Cover != null)
                yield return Visuals.Cover;
            if (Visuals.Video != null)
                yield return Visuals.Video;
        }
    }
}

public sealed class UtzSongMetadata
{
    [JsonProperty("title", Required = Required.Always)]
    public string Title { get; init; } = string.Empty;

    [JsonProperty("artist", Required = Required.Always)]
    public string Artist { get; init; } = string.Empty;

    [JsonProperty("album")]
    public string? Album { get; init; }

    [JsonProperty("language")]
    public string? Language { get; init; }

    [JsonProperty("duration_seconds", Required = Required.Always)]
    public double DurationSeconds { get; init; }

    [JsonProperty("bpm")]
    public double? Bpm { get; init; }

    [JsonProperty("key")]
    public string? Key { get; init; }
}

public sealed class UtzAudioAssets
{
    [JsonProperty("instrumental", Required = Required.Always)]
    public UtzAsset Instrumental { get; init; } = new();

    [JsonProperty("guide_vocals")]
    public UtzAsset? GuideVocals { get; init; }

    [JsonProperty("original")]
    public UtzAsset? Original { get; init; }

    [JsonProperty("audio_offset_seconds")]
    public double AudioOffsetSeconds { get; init; }
}

public sealed class UtzChartAssets
{
    [JsonProperty("transcript", Required = Required.Always)]
    public UtzAsset Transcript { get; init; } = new();

    [JsonProperty("pitch_track", Required = Required.Always)]
    public UtzAsset PitchTrack { get; init; } = new();

    [JsonProperty("pitch_notes", Required = Required.Always)]
    public UtzAsset PitchNotes { get; init; } = new();
}

public sealed class UtzVisualAssets
{
    [JsonProperty("cover")]
    public UtzAsset? Cover { get; init; }

    [JsonProperty("video")]
    public UtzAsset? Video { get; init; }
}

public sealed class UtzScoringConfig
{
    [JsonProperty("engine", Required = Required.Always)]
    public string Engine { get; init; } = string.Empty;

    [JsonProperty("version", Required = Required.Always)]
    public int Version { get; init; }

    [JsonProperty("octave_tolerance")]
    public bool OctaveTolerance { get; init; }
}

public sealed class UtzProvenance
{
    [JsonProperty("generator")]
    public string? Generator { get; init; }

    [JsonProperty("source")]
    public string? Source { get; init; }

    [JsonProperty("rights")]
    public string? Rights { get; init; }
}

public sealed class UtzAsset
{
    [JsonProperty("path", Required = Required.Always)]
    public string Path { get; init; } = string.Empty;

    [JsonProperty("media_type", Required = Required.Always)]
    public string MediaType { get; init; } = string.Empty;

    [JsonProperty("sha256", Required = Required.Always)]
    public string Sha256 { get; init; } = string.Empty;

    [JsonProperty("bytes", Required = Required.Always)]
    public long Bytes { get; init; }
}

public sealed class UtaTranscript
{
    [JsonProperty("language")]
    public string Language { get; init; } = string.Empty;

    [JsonProperty("segments", Required = Required.Always)]
    public IReadOnlyList<UtaTranscriptSegment> Segments { get; init; } = new List<UtaTranscriptSegment>();
}

public sealed class UtaTranscriptSegment
{
    [JsonProperty("text", Required = Required.Always)]
    public string Text { get; init; } = string.Empty;

    [JsonProperty("start", Required = Required.Always)]
    public double Start { get; init; }

    [JsonProperty("end", Required = Required.Always)]
    public double End { get; init; }

    [JsonProperty("words")]
    public IReadOnlyList<UtaTranscriptWord> Words { get; init; } = new List<UtaTranscriptWord>();
}

public sealed class UtaTranscriptWord
{
    [JsonProperty("word", Required = Required.Always)]
    public string Word { get; init; } = string.Empty;

    [JsonProperty("start", Required = Required.Always)]
    public double Start { get; init; }

    [JsonProperty("end", Required = Required.Always)]
    public double End { get; init; }

    [JsonProperty("reading")]
    public string? Reading { get; init; }

    [JsonProperty("estimated")]
    public bool Estimated { get; init; }
}

public sealed class UtaPitchTrack
{
    [JsonProperty("format_version", Required = Required.Always)]
    public int FormatVersion { get; init; }

    [JsonProperty("model")]
    public object? Model { get; init; }

    [JsonProperty("hop_seconds", Required = Required.Always)]
    public double HopSeconds { get; init; }

    [JsonProperty("frames", Required = Required.Always)]
    public IReadOnlyList<UtaPitchFrame> Frames { get; init; } = new List<UtaPitchFrame>();
}

public sealed class UtaPitchFrame
{
    [JsonProperty("time", Required = Required.Always)]
    public double Time { get; init; }

    [JsonProperty("hz")]
    public double? Hertz { get; init; }

    [JsonProperty("confidence", Required = Required.Always)]
    public double Confidence { get; init; }
}

public sealed class UtaPitchNoteChart
{
    [JsonProperty("format_version", Required = Required.Always)]
    public int FormatVersion { get; init; }

    [JsonProperty("notes", Required = Required.Always)]
    public IReadOnlyList<UtaPitchNote> Notes { get; init; } = new List<UtaPitchNote>();
}

public sealed class UtaPitchNote
{
    [JsonProperty("start", Required = Required.Always)]
    public double Start { get; init; }

    [JsonProperty("end", Required = Required.Always)]
    public double End { get; init; }

    [JsonProperty("midi", Required = Required.Always)]
    public int Midi { get; init; }

    [JsonProperty("confidence", Required = Required.Always)]
    public double Confidence { get; init; }

    [JsonProperty("kind")]
    public UtaPitchNoteKind Kind { get; init; }
}

public enum UtaPitchNoteKind
{
    Normal,
    Golden,
    Freestyle,
    Rap,
    GoldenRap,
}

public sealed class UtaBeatmapMetadata
{
    [JsonProperty("package_id", Required = Required.Always)]
    public string PackageId { get; init; } = string.Empty;

    [JsonProperty("octave_tolerance")]
    public bool OctaveTolerance { get; init; }

    [JsonProperty("guide_vocals_file")]
    public string? GuideVocalsFile { get; init; }

    [JsonProperty("original_audio_file")]
    public string? OriginalAudioFile { get; init; }

    [JsonProperty("centre_midi", Required = Required.Always)]
    public int CentreMidi { get; init; }
}
