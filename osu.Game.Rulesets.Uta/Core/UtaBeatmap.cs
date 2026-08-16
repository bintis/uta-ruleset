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

    protected override IEnumerable<UtaHitObject> ConvertHitObject(HitObject original, IBeatmap beatmap, CancellationToken cancellationToken)
        => Array.Empty<UtaHitObject>();

    protected override Beatmap<UtaHitObject> CreateBeatmap() => new UtaBeatmap();
}
