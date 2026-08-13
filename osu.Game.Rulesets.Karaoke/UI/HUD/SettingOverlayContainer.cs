// Copyright (c) andy840119 <andy840119@gmail.com>. Licensed under the GPL Licence.
// See the LICENCE file in the repository root for full licence text.

using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Input.Bindings;
using osu.Framework.Input.Events;
using osu.Game.Rulesets.Karaoke.Extensions;
using osu.Game.Rulesets.Karaoke.UI;
using osu.Game.Rulesets.UI;

namespace osu.Game.Rulesets.Karaoke.UI.HUD;

/// <summary>
/// Routes karaoke's in-game settings shortcut to lazer's native settings overlay.
/// </summary>
public partial class SettingOverlayContainer : CompositeDrawable, IKeyBindingHandler<KaraokeAction>
{
    private KaraokeSettingsOverlay? settingsOverlay;

    [BackgroundDependencyLoader(true)]
    private void load(DrawableRuleset? drawableRuleset, OsuGame? game)
    {
        if (drawableRuleset is DrawableKaraokeRuleset karaokeRuleset)
        {
            settingsOverlay = new KaraokeSettingsOverlay(karaokeRuleset.Config)
            {
                RelativeSizeAxes = Axes.Y,
            };

            if (game?.GetSettingsPlacementContainer() is { } placement)
                placement.Add(settingsOverlay);
            else
                AddInternal(settingsOverlay);
        }
    }

    public void ToggleGeneralSettingsOverlay()
    {
        if (settingsOverlay == null)
            return;

        if (settingsOverlay.State.Value == Visibility.Visible)
        {
            settingsOverlay.Hide();
            return;
        }

        // Keep gameplay running while the quick-settings cards are open.
        Schedule(settingsOverlay.Show);
    }

    public virtual bool OnPressed(KeyBindingPressEvent<KaraokeAction> e)
    {
        switch (e.Action)
        {
            // Open adjustment overlay
            case KaraokeAction.OpenPanel:
                ToggleGeneralSettingsOverlay();
                return true;

            default:
                return false;
        }
    }

    public virtual void OnReleased(KeyBindingReleaseEvent<KaraokeAction> e)
    {
    }

    protected override void Dispose(bool isDisposing)
    {
        settingsOverlay?.Expire();
        base.Dispose(isDisposing);
    }
}
