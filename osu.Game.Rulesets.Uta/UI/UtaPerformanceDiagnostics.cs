// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENSE file in the repository root for full licence text.

using System;
using System.Diagnostics;
using System.Threading;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Logging;
using osu.Game.Rulesets.Uta.Configuration;

namespace osu.Game.Rulesets.Uta.UI;

internal sealed partial class UtaPerformanceDiagnostics : Component
{
    private const double report_interval_ms = 5000;

    private readonly BindableBool enabled = new();
    private UtaAudioSettingsState settings = null!;
    private long intervalStart;
    private long previousUpdate;
    private long maximumUpdateGap;
    private int updateCount;
    private int generation0Collections;
    private int generation1Collections;
    private int generation2Collections;

    [BackgroundDependencyLoader]
    private void load(UtaAudioSettingsState settings)
    {
        this.settings = settings;
        enabled.BindTo(settings.DebugDiagnostics);
        enabled.BindValueChanged(_ => reset(), true);
    }

    protected override void Update()
    {
        base.Update();
        if (!enabled.Value)
            return;

        long now = Stopwatch.GetTimestamp();
        if (previousUpdate != 0)
            maximumUpdateGap = Math.Max(maximumUpdateGap, now - previousUpdate);
        previousUpdate = now;
        updateCount++;

        TimeSpan elapsed = Stopwatch.GetElapsedTime(intervalStart, now);
        if (elapsed.TotalMilliseconds < report_interval_ms)
            return;

        int generation0 = GC.CollectionCount(0);
        int generation1 = GC.CollectionCount(1);
        int generation2 = GC.CollectionCount(2);
        double updateRate = updateCount / elapsed.TotalSeconds;
        double maximumGap = maximumUpdateGap * 1000.0 / Stopwatch.Frequency;
        Logger.Log(
            $"Uta debug frame: update={updateRate:0.0}/s max-gap={maximumGap:0.00}ms " +
            $"managed={GC.GetTotalMemory(false) / 1048576.0:0.0}MiB working-set={Environment.WorkingSet / 1048576.0:0.0}MiB " +
            $"gc=+{generation0 - generation0Collections}/+{generation1 - generation1Collections}/+{generation2 - generation2Collections} " +
            $"threadpool-threads={ThreadPool.ThreadCount} pending={ThreadPool.PendingWorkItemCount}");
        Logger.Log(
            $"Uta debug settings: key={settings.KeyShiftSemitones.Value:+0;-0;0}st " +
            $"latency-mic={settings.MicrophoneLatency.Value:+0;-0;0}ms " +
            $"latency-accompaniment={settings.AccompanimentLatency.Value:+0;-0;0}ms " +
            $"latency-lyrics={settings.LyricsLatency.Value:+0;-0;0}ms " +
            $"volume-bgm={settings.BackgroundMusicVolume.Value:P0} volume-vocals={settings.OriginalVocalsVolume.Value:P0} " +
            $"volume-monitor={settings.MicrophoneMonitorVolume.Value:P0} input-gain={settings.MicrophoneInputGain.Value:0.00}x " +
            $"mic='{device(settings.MicrophoneDevice.Value)}' mic-output='{device(settings.MicrophoneOutputDevice.Value)}' " +
            $"bgm-output='{device(settings.BackgroundMusicOutputDevice.Value)}' vocals-output='{device(settings.OriginalVocalsOutputDevice.Value)}'");

        generation0Collections = generation0;
        generation1Collections = generation1;
        generation2Collections = generation2;
        intervalStart = now;
        previousUpdate = now;
        maximumUpdateGap = 0;
        updateCount = 0;
    }

    private void reset()
    {
        intervalStart = Stopwatch.GetTimestamp() + Stopwatch.Frequency * 3 / 2;
        previousUpdate = 0;
        maximumUpdateGap = 0;
        updateCount = 0;
        generation0Collections = GC.CollectionCount(0);
        generation1Collections = GC.CollectionCount(1);
        generation2Collections = GC.CollectionCount(2);
    }

    private static string device(string value) => string.IsNullOrEmpty(value) ? "default" : value;

    protected override void Dispose(bool isDisposing)
    {
        enabled.UnbindAll();
        base.Dispose(isDisposing);
    }
}
