// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENCE file in the repository root for full licence text.

using System.Collections.Generic;
using osu.Framework.Bindables;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Filter;
using osu.Game.Rulesets.Mods;
using osu.Game.Screens.Select;
using osu.Game.Screens.Select.Filter;

namespace osu.Game.Rulesets.Karaoke.Beatmaps;

/// <summary>
/// Karaoke charts are authored specifically for singing and cannot be derived
/// from the hit objects of another ruleset. Keep song select limited to native
/// karaoke beatmaps even when lazer's global "show converted beatmaps" option is enabled.
/// </summary>
public class KaraokeFilterCriteria : IRulesetFilterCriteria
{
    public bool Matches(BeatmapInfo beatmapInfo, FilterCriteria criteria)
        => beatmapInfo.Ruleset.ShortName == KaraokeRuleset.SHORT_NAME;

    public bool TryParseCustomKeywordCriteria(string key, Operator op, string value) => false;

    public bool FilterMayChangeFromMods(FilterCriteria criteria, ValueChangedEvent<IReadOnlyList<Mod>> mods) => false;
}
