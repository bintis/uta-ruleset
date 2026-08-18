// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENSE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Objects.Types;
using osu.Game.Rulesets.Uta.Formats;
using osu.Game.Rulesets.Uta.Mods;
using osu.Game.Rulesets.Uta.Scoring;

namespace osu.Game.Rulesets.Uta.Core;

public sealed class UtaBeatmap : Beatmap<UtaHitObject>
{
    public string PackageId { get; set; } = string.Empty;

    public bool OctaveTolerance { get; set; }

    public int CentreMidi { get; set; } = 60;

    public string? GuideVocalsFile { get; set; }

    public string? OriginalAudioFile { get; set; }

    public IReadOnlyList<UtaTranscriptSegment> Transcript { get; set; } = Array.Empty<UtaTranscriptSegment>();
}

public class UtaHitObject : HitObject, IHasDuration
{
    public double Duration { get; set; }

    public double EndTime => StartTime + Duration;

    public override Judgement CreateJudgement() => new IgnoreJudgement();
}

public sealed class UtaNote : UtaHitObject
{
    /// <summary>
    /// Runtime-only switch. True by default; <see cref="UtaModRelax"/> turns it off.
    /// It is deliberately not part of the beatmap format: scoring is an
    /// explicit gameplay-mode choice rather than chart metadata.
    /// </summary>
    public bool ScoringEnabled { get; set; }

    public override Judgement CreateJudgement()
        => ScoringEnabled && UtaScoringBeatmapAdapter.IsScorable(this) ? new UtaJudgement() : new UtaIgnoredJudgement();

    public int? Midi { get; set; }

    public string NoteKind { get; set; } = "normal";

    public double TargetConfidence { get; set; } = 1;

    public int ScoringIndex { get; set; } = -1;
}

internal sealed class UtaMetadataHitObject : UtaHitObject
{
    public UtaBeatmapMetadata Metadata { get; init; } = new();
}

public sealed class UtaBeatmapConverter : BeatmapConverter<UtaHitObject>
{
    private readonly IBeatmap source;

    public UtaBeatmapConverter(IBeatmap beatmap, Ruleset ruleset)
        : base(beatmap, ruleset)
    {
        source = beatmap;
    }

    // Song select can briefly retain a beatmap from the previous ruleset while
    // rebuilding filters and the Mods overlay. An empty Uta conversion keeps
    // that transition valid; UtaFilterCriteria still hides non-Uta beatmaps.
    public override bool CanConvert() => true;

    protected override Beatmap<UtaHitObject> ConvertBeatmap(IBeatmap original, CancellationToken cancellationToken)
    {
        var converted = (UtaBeatmap)base.ConvertBeatmap(original, cancellationToken);

        // The decoder-cached beatmap already contains UtaHitObjects, so the base converter
        // reuses those instances. A same-chart Player.Restart then ApplyDefaults them on a
        // load thread while the outgoing play still has DrawableHitObjects subscribed.
        converted.HitObjects = converted.HitObjects.Select(CloneForPlayable).ToList();

        UtaMetadataHitObject? carrier = converted.HitObjects.OfType<UtaMetadataHitObject>().SingleOrDefault();

        if (carrier == null)
            return converted;

        converted.PackageId = carrier.Metadata.PackageId;
        converted.OctaveTolerance = carrier.Metadata.OctaveTolerance;
        converted.GuideVocalsFile = carrier.Metadata.GuideVocalsFile;
        converted.OriginalAudioFile = carrier.Metadata.OriginalAudioFile;
        converted.CentreMidi = carrier.Metadata.CentreMidi;
        converted.Transcript = carrier.Metadata.Transcript;
        converted.HitObjects.Remove(carrier);
        return converted;
    }

    internal static UtaHitObject CloneForPlayable(UtaHitObject source) => source switch
    {
        UtaMetadataHitObject metadata => new UtaMetadataHitObject
        {
            Metadata = metadata.Metadata,
            StartTime = metadata.StartTime,
            Duration = metadata.Duration,
        },
        UtaNote note => new UtaNote
        {
            StartTime = note.StartTime,
            Duration = note.Duration,
            Midi = note.Midi,
            NoteKind = note.NoteKind,
            TargetConfidence = note.TargetConfidence,
            ScoringIndex = note.ScoringIndex,
            ScoringEnabled = note.ScoringEnabled,
        },
        _ => new UtaHitObject
        {
            StartTime = source.StartTime,
            Duration = source.Duration,
        },
    };

    protected override IEnumerable<UtaHitObject> ConvertHitObject(HitObject original, IBeatmap beatmap, CancellationToken cancellationToken)
        => Array.Empty<UtaHitObject>();

    protected override Beatmap<UtaHitObject> CreateBeatmap() => new UtaBeatmap();
}
