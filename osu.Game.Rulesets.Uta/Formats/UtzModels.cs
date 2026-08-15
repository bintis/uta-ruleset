// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENSE file in the repository root for full licence text.

using System;
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

    [JsonPropertyName("analysis")]
    public UtzAnalysisAssets? Analysis { get; init; }

    [JsonPropertyName("visuals")]
    public UtzVisualAssets Visuals { get; init; } = new();

    [JsonPropertyName("scoring")]
    public UtzScoringConfig? Scoring { get; init; }

    [JsonPropertyName("required_features")]
    public IReadOnlyList<string> RequiredFeatures { get; init; } = Array.Empty<string>();

    [JsonPropertyName("optional_features")]
    public IReadOnlyList<string> OptionalFeatures { get; init; } = Array.Empty<string>();

    [JsonPropertyName("provenance")]
    public UtzProvenance Provenance { get; init; } = new();

    /// <summary>
    /// UTZ 0.2 replaces the three parallel 0.1 chart assets with a single vocal
    /// chart and demotes pitch analysis to optional evidence; readers branch on
    /// this to know which chart shape to expect.
    /// </summary>
    [JsonIgnore]
    public bool IsFormatV2 => FormatVersion.StartsWith("0.2.", StringComparison.Ordinal);

    [JsonIgnore]
    public IEnumerable<UtzAsset> Assets
    {
        get
        {
            yield return Audio.Instrumental;

            if (Charts.Transcript != null)
                yield return Charts.Transcript;
            if (Charts.PitchTrack != null)
                yield return Charts.PitchTrack;
            if (Charts.PitchNotes != null)
                yield return Charts.PitchNotes;
            if (Charts.Vocal != null)
                yield return Charts.Vocal;
            if (Analysis?.PitchEvidence != null)
                yield return Analysis.PitchEvidence;

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

    [JsonPropertyName("title_sort")]
    public string? TitleSort { get; init; }

    [JsonPropertyName("artist_sort")]
    public string? ArtistSort { get; init; }

    [JsonPropertyName("genre")]
    public string? Genre { get; init; }

    [JsonPropertyName("year")]
    public int? Year { get; init; }

    /// <summary>Chart author credited by the 0.2 song block, distinct from the recording artist.</summary>
    [JsonPropertyName("creator")]
    public string? Creator { get; init; }

    [JsonPropertyName("tags")]
    public IReadOnlyList<string> Tags { get; init; } = Array.Empty<string>();

    [JsonPropertyName("preview_start_seconds")]
    public double? PreviewStartSeconds { get; init; }
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

    /// <summary>0.1 only; 0.2 bakes any shift into authored note times instead.</summary>
    [JsonPropertyName("audio_offset_seconds")]
    public double? AudioOffsetSeconds { get; init; }
}

public sealed class UtzChartAssets
{
    /// <summary>0.1 only.</summary>
    [JsonPropertyName("transcript")]
    public UtzAsset? Transcript { get; init; }

    /// <summary>0.1 only.</summary>
    [JsonPropertyName("pitch_track")]
    public UtzAsset? PitchTrack { get; init; }

    /// <summary>0.1 only.</summary>
    [JsonPropertyName("pitch_notes")]
    public UtzAsset? PitchNotes { get; init; }

    /// <summary>0.2 only: the single authoritative vocal chart.</summary>
    [JsonPropertyName("vocal")]
    public UtzAsset? Vocal { get; init; }
}

public sealed class UtzAnalysisAssets
{
    /// <summary>0.2 only; optional frame-level evidence, never the scoring chart.</summary>
    [JsonPropertyName("pitch_evidence")]
    public UtzAsset? PitchEvidence { get; init; }
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
    public double End { get; set; }

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

    /// <summary>
    /// Null for notes projected from a 0.2 vocal chart note that does not use
    /// pitch scoring (rap/spoken/rhythm/none); 0.1 pitch notes always carry a value.
    /// Not marked JsonRequired: the internal @utanote wire payload omits null
    /// values on write (WhenWritingNull), so the key may legitimately be absent.
    /// </summary>
    [JsonPropertyName("midi")]
    public int? Midi { get; init; }

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
    GoldenFreestyle,
    Rap,
    GoldenRap,
    Spoken,
    GoldenSpoken,
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

// UTZ 0.2 vocal chart (vocal-chart-v1.schema.json): track -> phrase -> note,
// with authored pitch/scoring/lyric-token data replacing the 0.1 transcript,
// pitch-track and pitch-notes trio. See format/utz-v0.2.md for the prose spec.

public sealed class UtaVocalChart
{
    [JsonRequired]
    [JsonPropertyName("format")]
    public string Format { get; init; } = string.Empty;

    [JsonRequired]
    [JsonPropertyName("format_version")]
    public string FormatVersion { get; init; } = string.Empty;

    [JsonRequired]
    [JsonPropertyName("timebase")]
    public long Timebase { get; init; }

    [JsonPropertyName("language")]
    public string? Language { get; init; }

    [JsonRequired]
    [JsonPropertyName("tracks")]
    public IReadOnlyList<UtaVocalTrack> Tracks { get; init; } = Array.Empty<UtaVocalTrack>();
}

public enum UtaVocalTrackRole
{
    Lead,
    Harmony,
    Backing,
    Adlib,
}

public sealed class UtaVocalTrack
{
    [JsonRequired]
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonRequired]
    [JsonPropertyName("role")]
    public UtaVocalTrackRole Role { get; init; }

    /// <summary>UltraStar-style duet part counted from 1; null/absent means unassigned.</summary>
    [JsonPropertyName("part")]
    public int? Part { get; init; }

    [JsonPropertyName("singer")]
    public string? Singer { get; init; }

    [JsonPropertyName("scoring_enabled")]
    public bool ScoringEnabled { get; init; } = true;

    [JsonRequired]
    [JsonPropertyName("phrases")]
    public IReadOnlyList<UtaVocalPhrase> Phrases { get; init; } = Array.Empty<UtaVocalPhrase>();
}

/// <summary>One displayed lyric line. Phrases do not nest.</summary>
public sealed class UtaVocalPhrase
{
    [JsonRequired]
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonRequired]
    [JsonPropertyName("notes")]
    public IReadOnlyList<UtaVocalNote> Notes { get; init; } = Array.Empty<UtaVocalNote>();
}

public enum UtaVocalMode
{
    Pitched,
    Rap,
    Spoken,
    Freestyle,
}

public enum UtaVocalBonus
{
    Normal,
    Golden,
}

public enum UtaVocalScoringMode
{
    Pitch,
    Rhythm,
    None,
}

public sealed class UtaVocalPitch
{
    [JsonRequired]
    [JsonPropertyName("midi")]
    public int Midi { get; init; }

    [JsonPropertyName("cents")]
    public int Cents { get; init; }
}

public sealed class UtaVocalScoring
{
    [JsonRequired]
    [JsonPropertyName("mode")]
    public UtaVocalScoringMode Mode { get; init; }

    [JsonPropertyName("weight")]
    public double Weight { get; init; } = 1;
}

public sealed class UtaVocalNote
{
    [JsonRequired]
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    [JsonRequired]
    [JsonPropertyName("start")]
    public long Start { get; init; }

    [JsonRequired]
    [JsonPropertyName("duration")]
    public long Duration { get; init; }

    [JsonRequired]
    [JsonPropertyName("pitch")]
    public UtaVocalPitch? Pitch { get; init; }

    [JsonRequired]
    [JsonPropertyName("vocal_mode")]
    public UtaVocalMode VocalMode { get; init; }

    [JsonRequired]
    [JsonPropertyName("bonus")]
    public UtaVocalBonus Bonus { get; init; }

    [JsonRequired]
    [JsonPropertyName("scoring")]
    public UtaVocalScoring Scoring { get; init; } = new();

    [JsonRequired]
    [JsonPropertyName("lyrics")]
    public IReadOnlyList<UtaLyricToken> Lyrics { get; init; } = Array.Empty<UtaLyricToken>();
}

public enum UtaLyricJoin
{
    None,
    Space,
}

/// <summary>
/// Covers both lyric token shapes from the schema's <c>oneOf</c>: a text token
/// (id/text/join_before, optional reading/phonemes) or a continuation token
/// (only continuation_of, referencing a text token earlier in the same track).
/// </summary>
public sealed class UtaLyricToken
{
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("text")]
    public string? Text { get; init; }

    [JsonPropertyName("join_before")]
    public UtaLyricJoin JoinBefore { get; init; }

    [JsonPropertyName("reading")]
    public string? Reading { get; init; }

    [JsonPropertyName("phonemes")]
    public string? Phonemes { get; init; }

    [JsonPropertyName("continuation_of")]
    public string? ContinuationOf { get; init; }

    [JsonIgnore]
    public bool IsContinuation => ContinuationOf != null;
}

/// <summary>Optional frame-level pitch evidence (pitch-evidence-v1.schema.json). Editor aid only, never the scoring chart.</summary>
public sealed class UtaPitchEvidence
{
    [JsonRequired]
    [JsonPropertyName("format")]
    public string Format { get; init; } = string.Empty;

    [JsonRequired]
    [JsonPropertyName("format_version")]
    public string FormatVersion { get; init; } = string.Empty;

    [JsonRequired]
    [JsonPropertyName("timebase")]
    public long Timebase { get; init; }

    [JsonRequired]
    [JsonPropertyName("start")]
    public long Start { get; init; }

    [JsonRequired]
    [JsonPropertyName("hop")]
    public long Hop { get; init; }

    [JsonRequired]
    [JsonPropertyName("frequency_hz")]
    public IReadOnlyList<double?> FrequencyHz { get; init; } = Array.Empty<double?>();

    [JsonRequired]
    [JsonPropertyName("confidence")]
    public IReadOnlyList<double> Confidence { get; init; } = Array.Empty<double>();
}
