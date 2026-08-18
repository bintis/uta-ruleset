// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENSE file in the repository root for full licence text.

using osu.Framework.Bindables;

namespace osu.Game.Rulesets.Uta.Core;

/// <summary>
/// Session-scoped mode state shared by desktop controls and the optional remote.
/// It is initialised from selected MODs, but may be changed during practice without
/// mutating lazer's persisted MOD selection.
/// </summary>
internal sealed class UtaRuntimeModeState
{
    public readonly BindableBool OriginalVocalsEnabled = new();
    public readonly BindableBool OctaveFoldEnabled = new();
}
