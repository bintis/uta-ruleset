// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENSE file in the repository root for full licence text.

using System;
using osu.Framework.Bindables;

namespace osu.Game.Rulesets.Uta.Configuration;

/// <summary>
/// One shared set of audio controls for a gameplay session. This keeps the
/// native volume overlay, quick settings and all three audio consumers on the
/// same bindables even when lazer creates more than one config-manager view.
/// </summary>
internal sealed class UtaAudioSettingsState : IDisposable
{
    public readonly BindableDouble BackgroundMusicVolume = new();
    public readonly BindableFloat OriginalVocalsVolume = new();
    public readonly Bindable<string> BackgroundMusicOutputDevice = new();
    public readonly Bindable<string> OriginalVocalsOutputDevice = new();
    public readonly Bindable<string> MicrophoneDevice = new();
    public readonly Bindable<string> MicrophoneOutputDevice = new();
    public readonly BindableFloat MicrophoneInputGain = new();
    public readonly BindableFloat MicrophoneMonitorVolume = new();
    public readonly BindableFloat MicrophoneLatency = new();
    public readonly BindableFloat KeyShiftSemitones = new();
    public readonly BindableFloat AccompanimentLatency = new();
    public readonly BindableFloat LyricsLatency = new();
    public readonly BindableBool DebugDiagnostics = new();
    public readonly BindableFloat PitchSamplingInterval = new();

    private bool initialised;

    public void Initialise(UtaRulesetConfigManager config)
    {
        if (initialised)
            return;

        BackgroundMusicVolume.BindTo(config.GetBindable<double>(UtaRulesetSetting.BackgroundMusicVolume));
        OriginalVocalsVolume.BindTo(config.GetBindable<float>(UtaRulesetSetting.OriginalVocalsVolume));
        BackgroundMusicOutputDevice.BindTo(config.GetBindable<string>(UtaRulesetSetting.BackgroundMusicOutputDevice));
        OriginalVocalsOutputDevice.BindTo(config.GetBindable<string>(UtaRulesetSetting.OriginalVocalsOutputDevice));
        MicrophoneDevice.BindTo(config.GetBindable<string>(UtaRulesetSetting.MicrophoneDevice));
        MicrophoneOutputDevice.BindTo(config.GetBindable<string>(UtaRulesetSetting.MicrophoneOutputDevice));
        MicrophoneInputGain.BindTo(config.GetBindable<float>(UtaRulesetSetting.MicrophoneInputGain));
        MicrophoneMonitorVolume.BindTo(config.GetBindable<float>(UtaRulesetSetting.MicrophoneMonitorVolume));
        MicrophoneLatency.BindTo(config.GetBindable<float>(UtaRulesetSetting.MicrophoneLatency));
        KeyShiftSemitones.BindTo(config.GetBindable<float>(UtaRulesetSetting.KeyShiftSemitones));
        AccompanimentLatency.BindTo(config.GetBindable<float>(UtaRulesetSetting.AccompanimentLatency));
        LyricsLatency.BindTo(config.GetBindable<float>(UtaRulesetSetting.LyricsLatency));
        DebugDiagnostics.BindTo(config.GetBindable<bool>(UtaRulesetSetting.DebugDiagnostics));
        PitchSamplingInterval.BindTo(config.GetBindable<float>(UtaRulesetSetting.PitchSamplingInterval));
        initialised = true;
    }

    public void Dispose()
    {
        BackgroundMusicVolume.UnbindAll();
        OriginalVocalsVolume.UnbindAll();
        BackgroundMusicOutputDevice.UnbindAll();
        OriginalVocalsOutputDevice.UnbindAll();
        MicrophoneDevice.UnbindAll();
        MicrophoneOutputDevice.UnbindAll();
        MicrophoneInputGain.UnbindAll();
        MicrophoneMonitorVolume.UnbindAll();
        MicrophoneLatency.UnbindAll();
        KeyShiftSemitones.UnbindAll();
        AccompanimentLatency.UnbindAll();
        LyricsLatency.UnbindAll();
        DebugDiagnostics.UnbindAll();
        PitchSamplingInterval.UnbindAll();
    }
}
