// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENSE file in the repository root for full licence text.

using NUnit.Framework;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Uta.Mods;
using osu.Game.Rulesets.Uta.Remote;

namespace osu.Game.Rulesets.Uta.Tests;

/// <summary>
/// Source-level leftover VOX contract. Play 1 is no-mod (ear + BGM only).
/// Play 2 with VOX, then a 切歌 that constructs with <c>mods: []</c>, must
/// keep original vocals on. Falling-edge live mods is the explicit off.
/// </summary>
[TestFixture]
[Explicit("Interactive VisualTestRunner / TestBrowser. Headless testhost has no BASS devices.")]
public partial class TestSceneUtaOriginalVocals : UtaPlayerTestScene
{
    protected override bool HasCustomSteps => true;

    protected override bool UseFreshStoragePerRun => true;

    [Test]
    public void TestPlayWithoutModsLeavesOriginalVocalsOff()
    {
        AddStep("clear original-vocals preference", () => UtaRulesetRuntime.Instance.RememberOriginalVocals(false));
        CreateTest();
        AddAssert("original vocals disabled", () => !DrawableUta.RuntimeModes.OriginalVocalsEnabled.Value);
        AddAssert("preference stays off", () => !UtaRulesetRuntime.Instance.OriginalVocalsPreferred);
    }

    [Test]
    public void TestVoxModEnablesOriginalVocals()
    {
        AddStep("clear original-vocals preference", () => UtaRulesetRuntime.Instance.RememberOriginalVocals(false));
        AddStep("load player with VOX", () => LoadPlayer(new Mod[] { new UtaModOriginalVocals() }));
        AddUntilStep("player loaded", () => Player.IsLoaded && Player.Alpha == 1);
        AddAssert("original vocals enabled", () => DrawableUta.RuntimeModes.OriginalVocalsEnabled.Value);
        AddAssert("preference remembered", () => UtaRulesetRuntime.Instance.OriginalVocalsPreferred);
    }

    [Test]
    public void TestEmptyConstructorAfterVoxKeepsOriginalVocals()
    {
        AddStep("prefer original vocals", () => UtaRulesetRuntime.Instance.RememberOriginalVocals(true));
        CreateTest();
        AddAssert(
            "empty constructor after 切歌 keeps vocals",
            () => DrawableUta.RuntimeModes.OriginalVocalsEnabled.Value);
        AddAssert("preference still on", () => UtaRulesetRuntime.Instance.OriginalVocalsPreferred);
    }
}
