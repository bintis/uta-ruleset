// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENSE file in the repository root for full licence text.

using System.Reflection;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Audio;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Logging;
using osu.Framework.Platform;
using osu.Framework.Testing;
using osu.Game;
using osu.Game.Graphics.Sprites;
using osu.Game.Overlays;
using osu.Game.Overlays.Volume;
using osu.Game.Rulesets.Uta.Configuration;

namespace osu.Game.Rulesets.Uta.UI;

/// <summary>
/// Maps lazer's native three-meter volume overlay to Uta's BGM, vocal and
/// microphone-monitor levels while gameplay is active. Native scroll selection,
/// dragging and animations remain unchanged.
/// </summary>
internal sealed partial class UtaVolumeOverlayExtension : Component
{
    private readonly BindableFloat vocalsConfig = new();
    private readonly BindableFloat microphoneConfig = new();
    private readonly BindableDouble backgroundMusicConfig = new();

    private VolumeOverlay? volumeOverlay;
    private AudioManager? audioManager;
    private GameHost? host;
    private VolumeMeter? microphoneMeter;
    private VolumeMeter? backgroundMusicMeter;
    private VolumeMeter? vocalsMeter;
    private OsuSpriteText? microphoneLabel;
    private OsuSpriteText? backgroundMusicLabel;
    private OsuSpriteText? vocalsLabel;
    private string microphoneLabelText = string.Empty;
    private string backgroundMusicLabelText = string.Empty;
    private string vocalsLabelText = string.Empty;
    private bool remapped;
    private bool suppressMeterWrites;
    private readonly IBindable<Visibility> settingsOverlayState = new Bindable<Visibility>();

    [BackgroundDependencyLoader(true)]
    private void load(VolumeOverlay? overlay, AudioManager audio, GameHost host, UtaAudioSettingsState audioSettings, UtaQuickSettingsOverlay? settingsOverlay)
    {
        volumeOverlay = overlay;
        audioManager = audio;
        this.host = host;
        vocalsConfig.BindTo(audioSettings.OriginalVocalsVolume);
        microphoneConfig.BindTo(audioSettings.MicrophoneMonitorVolume);
        backgroundMusicConfig.BindTo(audioSettings.BackgroundMusicVolume);
        Schedule(remap);

        // The O settings panel's own volume sliders share these exact bindables (BGM/vocals/mic
        // monitor), so opening it for the first time (its children lazy-load then) re-applies
        // the current value through PlayerSliderBar's own bind step - which echoes straight into
        // the remapped native meters below and pops lazer's volume HUD as an unintended
        // side-effect of merely opening a settings panel, not an actual user volume change.
        // Force it closed for the duration rather than chasing the exact echo path. Suppressing
        // only on the Visible transition edge was not enough - the echo can land a frame or two
        // later, after the panel's children finish lazily loading - so Update() below keeps
        // re-hiding every frame the panel is open instead of relying on a single edge trigger.
        if (settingsOverlay != null)
            settingsOverlayState.BindTo(settingsOverlay.State);
    }

    protected override void Update()
    {
        base.Update();

        if (settingsOverlayState.Value == Visibility.Visible)
            volumeOverlay?.Hide();
    }

    private void remap()
    {
        volumeOverlay ??= this.FindClosestParent<OsuGame>()?.ChildrenOfType<VolumeOverlay>().FirstOrDefault();

        if (volumeOverlay == null || audioManager == null)
        {
            Logger.Log("Uta could not access lazer's native volume overlay.", level: LogLevel.Error);
            return;
        }

        microphoneMeter = getMeter("volumeMeterEffect");
        backgroundMusicMeter = getMeter("volumeMeterMaster");
        vocalsMeter = getMeter("volumeMeterMusic");

        if (backgroundMusicMeter == null || microphoneMeter == null || vocalsMeter == null)
        {
            Logger.Log("Uta could not locate lazer's three native volume meters.", level: LogLevel.Error);
            return;
        }

        microphoneMeter.Bindable.UnbindFrom(audioManager.VolumeSample);
        backgroundMusicMeter.Bindable.UnbindFrom(audioManager.Volume);
        vocalsMeter.Bindable.UnbindFrom(audioManager.VolumeTrack);

        // The music meter is bound to lazer's track volume, which Uta forces to 0
        // while routed BGM is playing. Applying that 0 back into OriginalVocalsVolume
        // muted the packaged vocal track and cleared VOX until the user moved a slider.
        suppressMeterWrites = true;
        microphoneConfig.BindValueChanged(onMicrophoneConfigChanged, true);
        backgroundMusicConfig.BindValueChanged(onBackgroundMusicConfigChanged, true);
        vocalsConfig.BindValueChanged(onVocalsConfigChanged, true);
        microphoneMeter.Bindable.BindValueChanged(onMicrophoneMeterChanged);
        backgroundMusicMeter.Bindable.BindValueChanged(onBackgroundMusicMeterChanged);
        vocalsMeter.Bindable.BindValueChanged(onVocalsMeterChanged);
        microphoneMeter.Bindable.Value = microphoneConfig.Value;
        backgroundMusicMeter.Bindable.Value = backgroundMusicConfig.Value;
        vocalsMeter.Bindable.Value = vocalsConfig.Value;
        Schedule(() => suppressMeterWrites = false);

        microphoneLabel = findLabel(microphoneMeter);
        backgroundMusicLabel = findLabel(backgroundMusicMeter);
        vocalsLabel = findLabel(vocalsMeter);
        microphoneLabelText = microphoneLabel?.Text.ToString() ?? string.Empty;
        backgroundMusicLabelText = backgroundMusicLabel?.Text.ToString() ?? string.Empty;
        vocalsLabelText = vocalsLabel?.Text.ToString() ?? string.Empty;

        if (microphoneLabel != null)
            microphoneLabel.Text = "EAR MONITOR";
        if (backgroundMusicLabel != null)
            backgroundMusicLabel.Text = "BGM";
        if (vocalsLabel != null)
            vocalsLabel.Text = "ORIGINAL VOCALS";
        remapped = true;
        Logger.Log("Uta native volume overlay remapped to microphone, BGM and original vocals.");
    }

    private VolumeMeter? getMeter(string fieldName)
        => typeof(VolumeOverlay).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(volumeOverlay) as VolumeMeter;

    private static OsuSpriteText? findLabel(VolumeMeter meter)
        => meter.ChildrenOfType<OsuSpriteText>().LastOrDefault();

    private void onMicrophoneConfigChanged(ValueChangedEvent<float> value) => microphoneMeter!.Bindable.Value = value.NewValue;

    private void onBackgroundMusicConfigChanged(ValueChangedEvent<double> value) => backgroundMusicMeter!.Bindable.Value = value.NewValue;

    private void onVocalsConfigChanged(ValueChangedEvent<float> value) => vocalsMeter!.Bindable.Value = value.NewValue;

    private void onMicrophoneMeterChanged(ValueChangedEvent<double> value)
    {
        if (suppressMeterWrites)
            return;

        microphoneConfig.Value = (float)value.NewValue;
    }

    private void onBackgroundMusicMeterChanged(ValueChangedEvent<double> value)
    {
        if (suppressMeterWrites)
            return;

        backgroundMusicConfig.Value = value.NewValue;
    }

    private void onVocalsMeterChanged(ValueChangedEvent<double> value)
    {
        if (suppressMeterWrites)
            return;

        vocalsConfig.Value = (float)value.NewValue;
    }

    protected override void Dispose(bool isDisposing)
    {
        if (remapped && host != null && audioManager != null)
        {
            VolumeMeter? microphone = microphoneMeter;
            VolumeMeter? backgroundMusic = backgroundMusicMeter;
            VolumeMeter? vocals = vocalsMeter;
            OsuSpriteText? microphoneText = microphoneLabel;
            OsuSpriteText? backgroundMusicText = backgroundMusicLabel;
            OsuSpriteText? vocalsText = vocalsLabel;
            string oldMicrophoneText = microphoneLabelText;
            string oldBackgroundMusicText = backgroundMusicLabelText;
            string oldVocalsText = vocalsLabelText;
            AudioManager audio = audioManager;
            VolumeOverlay overlay = volumeOverlay!;

            microphone!.Bindable.ValueChanged -= onMicrophoneMeterChanged;
            backgroundMusic!.Bindable.ValueChanged -= onBackgroundMusicMeterChanged;
            vocals!.Bindable.ValueChanged -= onVocalsMeterChanged;
            microphoneConfig.ValueChanged -= onMicrophoneConfigChanged;
            backgroundMusicConfig.ValueChanged -= onBackgroundMusicConfigChanged;
            vocalsConfig.ValueChanged -= onVocalsConfigChanged;

            // DrawableRuleset disposal happens on lazer's asynchronous disposal
            // thread. Rebinding a VolumeMeter immediately raises its animation
            // callback, so the global overlay must be restored on the update thread.
            host.UpdateThread.Scheduler.Add(() =>
            {
                microphone?.Bindable.BindTo(audio.VolumeSample);
                backgroundMusic?.Bindable.BindTo(audio.Volume);
                vocals?.Bindable.BindTo(audio.VolumeTrack);

                if (microphoneText != null)
                    microphoneText.Text = oldMicrophoneText;
                if (backgroundMusicText != null)
                    backgroundMusicText.Text = oldBackgroundMusicText;
                if (vocalsText != null)
                    vocalsText.Text = oldVocalsText;

                overlay.Hide();
                overlay.FinishTransforms(false, nameof(Alpha));
            });

            remapped = false;
        }

        vocalsConfig.UnbindAll();
        microphoneConfig.UnbindAll();
        backgroundMusicConfig.UnbindAll();
        settingsOverlayState.UnbindAll();
        base.Dispose(isDisposing);
    }
}
