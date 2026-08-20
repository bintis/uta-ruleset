// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENSE file in the repository root for full licence text.

using System;
using System.Linq;
using osu.Game.Rulesets.Mods;

namespace osu.Game.Rulesets.Uta.Mods;

/// <summary>
/// Standard Daycore - Nightcore's slowed-down, pitched-down counterpart, replacing the plain
/// <c>UtaModHalfTime</c> (removed as redundant now that <see cref="UtaModPractice"/>'s live speed
/// slider already covers every rate from 50% to 150%).
/// </summary>
public sealed class UtaModDaycore : ModDaycore
{
    public override Type[] IncompatibleMods => base.IncompatibleMods.Append(typeof(UtaModNightcore))
                                                    .Concat(UtaModTranspose.AllTransposeModTypes)
                                                    .ToArray();
}
