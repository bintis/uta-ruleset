// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENSE file in the repository root for full licence text.

using System.Collections.Generic;
using osu.Framework.Bindables;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Filter;
using osu.Game.Rulesets.Mods;
using osu.Game.Screens.Select;
using osu.Game.Screens.Select.Filter;

namespace osu.Game.Rulesets.Uta.Core;

public sealed class UtaFilterCriteria : IRulesetFilterCriteria
{
    public bool Matches(BeatmapInfo beatmapInfo, FilterCriteria criteria)
        => beatmapInfo.Ruleset.ShortName == UtaRuleset.SHORT_NAME;

    public bool TryParseCustomKeywordCriteria(string key, Operator op, string value) => false;

    public bool FilterMayChangeFromMods(FilterCriteria criteria, ValueChangedEvent<IReadOnlyList<Mod>> mods) => false;
}
