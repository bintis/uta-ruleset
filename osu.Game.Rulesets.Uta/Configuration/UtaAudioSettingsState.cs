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
    }
}
