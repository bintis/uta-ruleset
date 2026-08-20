// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENSE file in the repository root for full licence text.

using System;
using System.Linq;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Uta.Core;

namespace osu.Game.Rulesets.Uta.Mods;

/// <summary>
/// Standard Nightcore - 1.5x speed with its distinctive pitched-up treatment and beat-synced
/// effects, replacing the plain <c>UtaModDoubleTime</c> (removed as redundant now that
/// <see cref="UtaModPractice"/>'s live speed slider already covers every rate from 50% to 150%).
/// BGM/VOX sync comes for free from <see cref="UI.UtaAudioController"/>, which already tracks the
/// gameplay clock's rate generically for any rate-adjusting mod.
/// </summary>
public sealed class UtaModNightcore : ModNightcore<UtaHitObject>
{
    public override Type[] IncompatibleMods => base.IncompatibleMods.Append(typeof(UtaModDaycore))
                                                    .Concat(UtaModTranspose.AllTransposeModTypes)
                                                    .ToArray();
}
