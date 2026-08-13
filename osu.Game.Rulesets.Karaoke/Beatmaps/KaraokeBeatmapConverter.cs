// Copyright (c) andy840119 <andy840119@gmail.com>. Licensed under the GPL Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Karaoke.Objects;
using osu.Game.Rulesets.Objects;

namespace osu.Game.Rulesets.Karaoke.Beatmaps;

public class KaraokeBeatmapConverter : BeatmapConverter<KaraokeHitObject>
{
    public KaraokeBeatmapConverter(IBeatmap beatmap, Ruleset ruleset)
        : base(beatmap, ruleset)
    {
    }

    // Song select's ruleset-specific filter is responsible for excluding foreign maps.
    // Keep conversion permissive because lazer may briefly request statistics for the
    // previously selected map while that filter is being applied; rejecting it produces
    // an unobserved BeatmapInvalidForRulesetException in the native title wedge.
    public override bool CanConvert() => true;

    protected override Beatmap<KaraokeHitObject> ConvertBeatmap(IBeatmap original, CancellationToken cancellationToken)
    {
        var beatmap = base.ConvertBeatmap(original, cancellationToken);

        // Apply property created from legacy decoder
        var propertyDictionary = beatmap.HitObjects.OfType<LegacyProperties>().FirstOrDefault();

        if (propertyDictionary == null)
            return beatmap;

        if (beatmap is not KaraokeBeatmap karaokeBeatmap)
            throw new InvalidCastException(nameof(beatmap));

        karaokeBeatmap.AvailableTranslationLanguages = propertyDictionary.AvailableTranslationLanguages;
        karaokeBeatmap.UtaPackageId = propertyDictionary.UtaPackageId;
        karaokeBeatmap.UtaOctaveTolerance = propertyDictionary.UtaOctaveTolerance;
        karaokeBeatmap.UtaGuideVocalsFile = propertyDictionary.UtaGuideVocalsFile;
        karaokeBeatmap.UtaOriginalAudioFile = propertyDictionary.UtaOriginalAudioFile;
        karaokeBeatmap.UtaTranscriptSegments = propertyDictionary.UtaTranscriptSegments;
        karaokeBeatmap.UtaCentreMidi = propertyDictionary.UtaCentreMidi;
        if (propertyDictionary.UtaPackageId != null)
        {
            karaokeBeatmap.Scorable = true;
            // UTZ lyrics are rendered from the word-accurate transcript metadata.
            // Keeping the compatibility Lyric objects would also make them invisible
            // scoring objects, preventing native gameplay completion in some maps.
            beatmap.HitObjects.RemoveAll(hitObject => hitObject is Lyric);
        }
        beatmap.HitObjects.Remove(propertyDictionary);

        return beatmap;
    }

    protected override IEnumerable<KaraokeHitObject> ConvertHitObject(HitObject original, IBeatmap beatmap, CancellationToken cancellationToken)
        => Array.Empty<KaraokeHitObject>();

    protected override Beatmap<KaraokeHitObject> CreateBeatmap() => new KaraokeBeatmap();
}
