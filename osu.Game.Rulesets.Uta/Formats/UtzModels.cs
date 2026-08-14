// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENSE file in the repository root for full licence text.

using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace osu.Game.Rulesets.Uta.Formats;

public sealed class UtzManifest
{
    [JsonRequired]
    [JsonPropertyName("format")]
    public string Format { get; init; } = string.Empty;

    [JsonRequired]
    [JsonPropertyName("format_version")]
    public string FormatVersion { get; init; } = string.Empty;

    [JsonRequired]
    [JsonPropertyName("package_id")]
    public string PackageId { get; init; } = string.Empty;

    [JsonRequired]
    [JsonPropertyName("revision")]
    public int Revision { get; init; }

    [JsonRequired]
    [JsonPropertyName("song")]
    public UtzSongMetadata Song { get; init; } = new();

    [JsonRequired]
    [JsonPropertyName("audio")]
    public UtzAudioAssets Audio { get; init; } = new();

    [JsonRequired]
    [JsonPropertyName("charts")]
    public UtzChartAssets Charts { get; init; } = new();

    [JsonPropertyName("visuals")]
    public UtzVisualAssets Visuals { get; init; } = new();

    [JsonRequired]
    [JsonPropertyName("scoring")]
    public UtzScoringConfig Scoring { get; init; } = new();

    [JsonPropertyName("provenance")]
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
    [JsonRequired]
    [JsonPropertyName("title")]
    public string Title { get; init; } = string.Empty;

    [JsonRequired]
    [JsonPropertyName("artist")]
    public string Artist { get; init; } = string.Empty;

    [JsonPropertyName("album")]
    public string? Album { get; init; }

    [JsonPropertyName("language")]
    public string? Language { get; init; }

    [JsonRequired]
    [JsonPropertyName("duration_seconds")]
    public double DurationSeconds { get; init; }

    [JsonPropertyName("bpm")]
    public double? Bpm { get; init; }

    [JsonPropertyName("key")]
    public string? Key { get; init; }
}

public sealed class UtzAudioAssets
{
    [JsonRequired]
    [JsonPropertyName("instrumental")]
    public UtzAsset Instrumental { get; init; } = new();

    [JsonPropertyName("guide_vocals")]
    public UtzAsset? GuideVocals { get; init; }

    [JsonPropertyName("original")]
    public UtzAsset? Original { get; init; }

    [JsonPropertyName("audio_offset_seconds")]
    public double AudioOffsetSeconds { get; init; }
}

public sealed class UtzChartAssets
{
    [JsonRequired]
    [JsonPropertyName("transcript")]
    public UtzAsset Transcript { get; init; } = new();

    [JsonRequired]
    [JsonPropertyName("pitch_track")]
    public UtzAsset PitchTrack { get; init; } = new();

    [JsonRequired]
    [JsonPropertyName("pitch_notes")]
    public UtzAsset PitchNotes { get; init; } = new();
}

public sealed class UtzVisualAssets
{
    [JsonPropertyName("cover")]
    public UtzAsset? Cover { get; init; }

    [JsonPropertyName("video")]
    public UtzAsset? Video { get; init; }
}

public sealed class UtzScoringConfig
{
    [JsonRequired]
    [JsonPropertyName("engine")]
    public string Engine { get; init; } = string.Empty;

    [JsonRequired]
    [JsonPropertyName("version")]
    public int Version { get; init; }

    [JsonPropertyName("octave_tolerance")]
    public bool OctaveTolerance { get; init; }
}

public sealed class UtzProvenance
{
    [JsonPropertyName("generator")]
    public string? Generator { get; init; }

    [JsonPropertyName("source")]
    public string? Source { get; init; }

    [JsonPropertyName("rights")]
    public string? Rights { get; init; }
}

public sealed class UtzAsset
{
    [JsonRequired]
    [JsonPropertyName("path")]
    public string Path { get; init; } = string.Empty;

    [JsonRequired]
    [JsonPropertyName("media_type")]
    public string MediaType { get; init; } = string.Empty;

    [JsonRequired]
    [JsonPropertyName("sha256")]
    public string Sha256 { get; init; } = string.Empty;

    [JsonRequired]
    [JsonPropertyName("bytes")]
    public long Bytes { get; init; }
}

public sealed class UtaTranscript
{
    [JsonPropertyName("language")]
    public string Language { get; init; } = string.Empty;

    [JsonRequired]
    [JsonPropertyName("segments")]
    public IReadOnlyList<UtaTranscriptSegment> Segments { get; init; } = new List<UtaTranscriptSegment>();
}

public sealed class UtaTranscriptSegment
{
    [JsonRequired]
    [JsonPropertyName("text")]
    public string Text { get; init; } = string.Empty;

    [JsonRequired]
    [JsonPropertyName("start")]
    public double Start { get; init; }

    [JsonRequired]
    [JsonPropertyName("end")]
    public double End { get; init; }

    [JsonPropertyName("words")]
    public IReadOnlyList<UtaTranscriptWord> Words { get; init; } = new List<UtaTranscriptWord>();
}

public sealed class UtaTranscriptWord
{
    [JsonRequired]
    [JsonPropertyName("word")]
    public string Word { get; init; } = string.Empty;

    [JsonRequired]
    [JsonPropertyName("start")]
    public double Start { get; init; }

    [JsonRequired]
    [JsonPropertyName("end")]
    public double End { get; init; }

    [JsonPropertyName("reading")]
    public string? Reading { get; init; }

    [JsonPropertyName("estimated")]
    public bool Estimated { get; init; }
}

public sealed class UtaPitchTrack
{
    [JsonRequired]
    [JsonPropertyName("format_version")]
    public int FormatVersion { get; init; }

    [JsonPropertyName("model")]
    public object? Model { get; init; }

    [JsonRequired]
    [JsonPropertyName("hop_seconds")]
    public double HopSeconds { get; init; }

    [JsonRequired]
    [JsonPropertyName("frames")]
    public IReadOnlyList<UtaPitchFrame> Frames { get; init; } = new List<UtaPitchFrame>();
}

public sealed class UtaPitchFrame
{
    [JsonRequired]
    [JsonPropertyName("time")]
    public double Time { get; init; }

    [JsonPropertyName("hz")]
    public double? Hertz { get; init; }

    [JsonRequired]
    [JsonPropertyName("confidence")]
    public double Confidence { get; init; }
}

public sealed class UtaPitchNoteChart
{
    [JsonRequired]
    [JsonPropertyName("format_version")]
    public int FormatVersion { get; init; }

    [JsonRequired]
    [JsonPropertyName("notes")]
    public IReadOnlyList<UtaPitchNote> Notes { get; init; } = new List<UtaPitchNote>();
}

public sealed class UtaPitchNote
{
    [JsonRequired]
    [JsonPropertyName("start")]
    public double Start { get; init; }

    [JsonRequired]
    [JsonPropertyName("end")]
    public double End { get; init; }

    [JsonRequired]
    [JsonPropertyName("midi")]
    public int Midi { get; init; }

    [JsonRequired]
    [JsonPropertyName("confidence")]
    public double Confidence { get; init; }

    [JsonPropertyName("kind")]
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
    [JsonRequired]
    [JsonPropertyName("package_id")]
    public string PackageId { get; init; } = string.Empty;

    [JsonPropertyName("octave_tolerance")]
    public bool OctaveTolerance { get; init; }

    [JsonPropertyName("guide_vocals_file")]
    public string? GuideVocalsFile { get; init; }

    [JsonPropertyName("original_audio_file")]
    public string? OriginalAudioFile { get; init; }

    [JsonRequired]
    [JsonPropertyName("centre_midi")]
    public int CentreMidi { get; init; }

    [JsonRequired]
    [JsonPropertyName("transcript")]
    public IReadOnlyList<UtaTranscriptSegment> Transcript { get; init; } = new List<UtaTranscriptSegment>();
}
