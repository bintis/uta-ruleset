// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENSE file in the repository root for full licence text.

using System;
using osu.Framework.Bindables;
using osu.Framework.Logging;
using osu.Game.Rulesets.Uta.Remote;

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
    public readonly BindableBool OriginalVocalsEnabled = new();
    public readonly Bindable<string> BackgroundMusicOutputDevice = new();
    public readonly Bindable<string> OriginalVocalsOutputDevice = new();
    public readonly Bindable<string> MicrophoneDevice = new();
    public readonly Bindable<string> MicrophoneOutputDevice = new();
    public readonly BindableFloat MicrophoneInputGain = new();
    public readonly BindableFloat MicrophoneMonitorVolume = new();
    public readonly BindableFloat MicrophoneLatency = new();
    public readonly BindableFloat KeyShiftSemitones = new();
    public readonly BindableDouble PlaybackTempo = new(1)
    {
        MinValue = 0.05,
        MaxValue = 2,
        Precision = 0.01,
    };
    public readonly BindableDouble RuntimeModFrequency = new(1)
    {
        MinValue = 0.25,
        MaxValue = 4,
        Precision = 0.01,
    };
    public readonly BindableFloat AccompanimentLatency = new();
    public readonly BindableFloat LyricsLatency = new();
    public readonly BindableBool DebugDiagnostics = new();
    public readonly BindableFloat PitchSamplingInterval = new();
    public readonly BindableFloat PhraseLoopLeadIn = new();

    private bool initialised;

    public void Initialise(UtaRulesetConfigManager config)
    {
        if (initialised)
            return;

        bindTwoWay(BackgroundMusicVolume, config.GetBindable<double>(UtaRulesetSetting.BackgroundMusicVolume));
        bindTwoWay(OriginalVocalsVolume, config.GetBindable<float>(UtaRulesetSetting.OriginalVocalsVolume));
        bindTwoWay(OriginalVocalsEnabled, config.GetBindable<bool>(UtaRulesetSetting.OriginalVocalsEnabled));
        OriginalVocalsEnabled.BindValueChanged(change => UtaRulesetRuntime.Instance.RememberOriginalVocals(change.NewValue));
        bindTwoWay(BackgroundMusicOutputDevice, config.GetBindable<string>(UtaRulesetSetting.BackgroundMusicOutputDevice));
        bindTwoWay(OriginalVocalsOutputDevice, config.GetBindable<string>(UtaRulesetSetting.OriginalVocalsOutputDevice));
        bindTwoWay(MicrophoneDevice, config.GetBindable<string>(UtaRulesetSetting.MicrophoneDevice));
        bindTwoWay(MicrophoneOutputDevice, config.GetBindable<string>(UtaRulesetSetting.MicrophoneOutputDevice));
        bindTwoWay(MicrophoneInputGain, config.GetBindable<float>(UtaRulesetSetting.MicrophoneInputGain));
        bindTwoWay(MicrophoneMonitorVolume, config.GetBindable<float>(UtaRulesetSetting.MicrophoneMonitorVolume));
        bindTwoWay(MicrophoneLatency, config.GetBindable<float>(UtaRulesetSetting.MicrophoneLatency));
        bindTwoWay(KeyShiftSemitones, config.GetBindable<float>(UtaRulesetSetting.KeyShiftSemitones));
        bindTwoWay(AccompanimentLatency, config.GetBindable<float>(UtaRulesetSetting.AccompanimentLatency));
        bindTwoWay(LyricsLatency, config.GetBindable<float>(UtaRulesetSetting.LyricsLatency));
        bindTwoWay(DebugDiagnostics, config.GetBindable<bool>(UtaRulesetSetting.DebugDiagnostics));
        bindTwoWay(PitchSamplingInterval, config.GetBindable<float>(UtaRulesetSetting.PitchSamplingInterval));
        bindTwoWay(PhraseLoopLeadIn, config.GetBindable<float>(UtaRulesetSetting.PhraseLoopLeadIn));
        initialised = true;

        if (DebugDiagnostics.Value)
        {
            Logger.Log($"Uta debug audio settings: initialised from config instance {config.GetHashCode()} - "
                       + $"mic-output loaded as '{MicrophoneOutputDevice.Value}' bgm-output='{BackgroundMusicOutputDevice.Value}'");
        }
    }

    internal static string ResolveSafeMonitorOutput(string capture, string monitorOutput, string backgroundOutput, string vocalsOutput)
    {
        if (!string.Equals(capture, monitorOutput, StringComparison.OrdinalIgnoreCase))
            return monitorOutput;
        if (!string.IsNullOrWhiteSpace(backgroundOutput)
            && !string.Equals(backgroundOutput, capture, StringComparison.OrdinalIgnoreCase))
            return backgroundOutput;
        if (!string.IsNullOrWhiteSpace(vocalsOutput)
            && !string.Equals(vocalsOutput, capture, StringComparison.OrdinalIgnoreCase))
            return vocalsOutput;

        return monitorOutput;
    }

    private static void bindTwoWay<T>(Bindable<T> session, Bindable<T> persistent)
    {
        session.BindTo(persistent);
        session.BindValueChanged(change => persistent.Value = change.NewValue);
    }

    public void Dispose()
    {
        BackgroundMusicVolume.UnbindAll();
        OriginalVocalsVolume.UnbindAll();
        OriginalVocalsEnabled.UnbindAll();
        BackgroundMusicOutputDevice.UnbindAll();
        OriginalVocalsOutputDevice.UnbindAll();
        MicrophoneDevice.UnbindAll();
        MicrophoneOutputDevice.UnbindAll();
        MicrophoneInputGain.UnbindAll();
        MicrophoneMonitorVolume.UnbindAll();
        MicrophoneLatency.UnbindAll();
        KeyShiftSemitones.UnbindAll();
        PlaybackTempo.UnbindAll();
        RuntimeModFrequency.UnbindAll();
        AccompanimentLatency.UnbindAll();
        LyricsLatency.UnbindAll();
        DebugDiagnostics.UnbindAll();
        PitchSamplingInterval.UnbindAll();
        PhraseLoopLeadIn.UnbindAll();
    }
}
