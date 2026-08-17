// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENSE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Logging;
using osu.Game.Rulesets.Uta.Configuration;
using osu.Game.Rulesets.Uta.Core;
using osu.Game.Rulesets.Uta.Performance;
using osu.Game.Rulesets.Uta.Scoring;
using osu.Game.Screens.Play;

namespace osu.Game.Rulesets.Uta.Recording;

/// <summary>
/// Gameplay-facing recording owner. The microphone callback sees only
/// <see cref="IUtaPcmCaptureSink.TryWrite"/>. Session state, timeline boundaries
/// and archive finalisation remain on/background from the gameplay thread.
/// </summary>
internal sealed partial class UtaRecordingRuntime : Component, IUtaPcmCaptureSink
{
    private readonly UtaBeatmap beatmap;
    private readonly UtaGameplayScoringController scoring;
    private readonly bool recordingEnabled;
    private readonly bool archiveEnabled;
    private readonly UtaRecordingSession session = new();
    private readonly Bindable<string> performanceRoot = new();
    private readonly BindableFloat microphoneLatency = new();
    private readonly BindableFloat inputGain = new();
    private readonly BindableFloat keyShift = new();
    private readonly Bindable<string> microphoneDevice = new();
    private readonly Bindable<string> microphoneOutput = new();

    private GameplayClockContainer gameplayClock = null!;
    private bool lastPaused;
    private double lastRate;
    private double lastSongTime;
    private bool archiveFinalising;
    private bool performanceArchived;
    private string? stagingPath;
    private readonly double naturalEndTime;
    private bool debugDiagnostics;

    public bool RecordingEnabled => recordingEnabled;

    public UtaRecordingProgress Progress { get; private set; }
        = new(UtaRecordingState.Ready, 0, 0, 0, null, UtaRecordingFaultReason.None, null);

    public event Action<UtaRecordingProgress>? ProgressChanged;

    public UtaRecordingRuntime(
        UtaBeatmap beatmap,
        UtaGameplayScoringController scoring,
        bool recordingEnabled,
        bool archiveEnabled)
    {
        this.beatmap = beatmap;
        this.scoring = scoring;
        this.recordingEnabled = recordingEnabled;
        this.archiveEnabled = archiveEnabled;
        naturalEndTime = beatmap.HitObjects.Count == 0
            ? 0
            : beatmap.HitObjects.OfType<UtaHitObject>().Max(h => h.EndTime);
        session.ProgressChanged += onProgress;
    }

    [BackgroundDependencyLoader]
    private void load(
        GameplayClockContainer gameplayClock,
        UtaRulesetConfigManager config,
        UtaAudioSettingsState audioSettings)
    {
        this.gameplayClock = gameplayClock;
        debugDiagnostics = config.GetBindable<bool>(UtaRulesetSetting.DebugDiagnostics).Value;
        performanceRoot.BindTo(config.GetBindable<string>(UtaRulesetSetting.PerformanceRootDirectory));
        microphoneLatency.BindTo(audioSettings.MicrophoneLatency);
        inputGain.BindTo(audioSettings.MicrophoneInputGain);
        keyShift.BindTo(audioSettings.KeyShiftSemitones);
        microphoneDevice.BindTo(audioSettings.MicrophoneDevice);
        microphoneOutput.BindTo(audioSettings.MicrophoneOutputDevice);

        lastPaused = gameplayClock.IsPaused.Value;
        lastRate = gameplayClock.Rate;
        lastSongTime = gameplayClock.CurrentTime;
        gameplayClock.OnSeek += onSeek;
    }

    protected override void Update()
    {
        base.Update();

        if (!archiveEnabled)
            return;

        if (recordingEnabled && session.State == UtaRecordingState.Ready && gameplayClock.IsRunning && !performanceArchived)
            startTake();

        bool paused = gameplayClock.IsPaused.Value;
        if (session.State is UtaRecordingState.Recording or UtaRecordingState.Paused)
        {
            if (paused != lastPaused)
            {
                if (paused)
                    session.Pause();
                else
                {
                    session.Resume();
                    beginSegment(UtaRecordingSegmentReason.Resume);
                }
            }

            if (!paused && Math.Abs(gameplayClock.Rate - lastRate) > 0.000001)
                beginSegment(UtaRecordingSegmentReason.PlaybackRateChanged);
        }

        // Scoring mode archives pitch/score data; Recording mode additionally
        // attaches the post-gain microphone take. With neither mod, no archive
        // is produced.
        if (!performanceArchived && !archiveFinalising && naturalEndTime > 0
            && gameplayClock.IsRunning && gameplayClock.CurrentTime >= naturalEndTime + 500)
            _ = finaliseAndArchiveAsync();

        lastPaused = paused;
        lastRate = gameplayClock.Rate;
        lastSongTime = gameplayClock.CurrentTime;
    }

    private void startTake()
    {
        string root = resolvePerformanceRoot();
        Guid takeId = Guid.NewGuid();
        string stagingDirectory = Path.Combine(root, "staging", takeId.ToString("D"));
        Directory.CreateDirectory(stagingDirectory);
        stagingPath = Path.Combine(stagingDirectory, "take.wav");

        session.StartDeferred(stagingPath, new UtaRecordingMetadata
        {
            TakeId = takeId,
            CalibratedLatencyMilliseconds = microphoneLatency.Value,
            InputGain = inputGain.Value,
            TransposeSemitones = (int)MathF.Round(keyShift.Value),
            InputDevice = microphoneDevice.Value,
            MonitorOutputDevice = microphoneOutput.Value,
        });
        beginSegment(UtaRecordingSegmentReason.GameplayStart);

        if (debugDiagnostics)
        {
            Logger.Log($"Uta debug recording: take started id={takeId} staging='{stagingPath}' "
                       + $"device='{microphoneDevice.Value}' gain={inputGain.Value:0.00}");
        }
    }

    private void beginSegment(UtaRecordingSegmentReason reason)
        => session.BeginTimelineSegment(
            checked((long)Math.Round(gameplayClock.CurrentTime * 1000, MidpointRounding.AwayFromZero)),
            gameplayClock.IsPaused.Value ? 0 : gameplayClock.Rate,
            scoring.TimelineEpoch,
            reason);

    private void onSeek()
    {
        if (session.State is not (UtaRecordingState.Recording or UtaRecordingState.Paused))
            return;

        UtaRecordingSegmentReason reason = gameplayClock.CurrentTime < lastSongTime
            ? UtaRecordingSegmentReason.BackwardSeek
            : UtaRecordingSegmentReason.ForwardSeek;
        beginSegment(reason);
    }

    bool IUtaPcmCaptureSink.TryWrite(
        ReadOnlySpan<float> interleavedSamples,
        int sampleRate,
        int channels,
        long captureEndTimestamp,
        float appliedInputGain)
        => recordingEnabled && session.TryWrite(
            interleavedSamples,
            sampleRate,
            channels,
            captureEndTimestamp,
            appliedInputGain);

    public bool TryWrite(
        ReadOnlySpan<float> interleavedSamples,
        int sampleRate,
        int channels,
        long captureEndTimestamp,
        float appliedInputGain)
        => ((IUtaPcmCaptureSink)this).TryWrite(interleavedSamples, sampleRate, channels, captureEndTimestamp, appliedInputGain);

    private async Task finaliseAndArchiveAsync()
    {
        if (archiveFinalising)
            return;

        archiveFinalising = true;
        try
        {
            UtaRecordingMetadata recording = await session.StopAsync().ConfigureAwait(false);
            string? path = stagingPath;
            bool hasRecording = path != null && File.Exists(path) && recording.FrameCount > 0;

            if (debugDiagnostics)
            {
                double clippedPercent = recording.FrameCount > 0 ? 100.0 * recording.ClippedSamples / recording.FrameCount : 0;
                Logger.Log($"Uta debug recording: take finalised hasRecording={hasRecording} frames={recording.FrameCount} "
                           + $"clipped={recording.ClippedSamples} ({clippedPercent:0.00}%) segments={recording.Segments.Count} path='{path}'");
            }

            UtaPerformanceScore score = scoring.CompletePerformance();
            var manifest = UtaPerformanceManifest.FromScore(
                new UtaPerformanceSongInfo
                {
                    PackageId = beatmap.PackageId,
                    BeatmapHash = string.Empty,
                    Title = beatmap.Metadata?.Title ?? string.Empty,
                    Artist = beatmap.Metadata?.Artist ?? string.Empty,
                },
                new UtaPerformanceSettingsSnapshot
                {
                    TransposeSemitones = (int)MathF.Round(keyShift.Value),
                    OctaveFold = beatmap.OctaveTolerance,
                    PlaybackRate = lastRate,
                    MicrophoneLatencyMilliseconds = microphoneLatency.Value,
                    InputGain = inputGain.Value,
                    PracticeSession = !scoring.Comparable,
                    TimelineEpoch = scoring.TimelineEpoch,
                },
                score);

            manifest.Phrases = scoring.GetPhraseResults()
                .Select(p => new UtaPerformancePhraseSummary
                {
                    PhraseIndex = p.PhraseIndex,
                    StartTimeMicroseconds = p.StartTimeMicroseconds,
                    EndTimeMicroseconds = p.EndTimeMicroseconds,
                    Text = p.Text,
                    PitchAccuracyPermille = p.PitchAccuracyPermille,
                    CoveragePermille = p.CoveragePermille,
                    StabilityPermille = p.StabilityPermille,
                    BiasCents = p.BiasCents,
                    MissedIntervals = p.MissedSections
                        .Select(m => new UtaMissedInterval(m.StartTimeMicroseconds, m.EndTimeMicroseconds))
                        .ToArray(),
                })
                .ToArray();

            if (hasRecording)
            {
                manifest.Recording = new UtaPerformanceRecordingInfo
                {
                    SampleRate = recording.SampleRate,
                    Channels = recording.Channels,
                    CalibratedLatencyMilliseconds = recording.CalibratedLatencyMilliseconds,
                    InputGain = recording.InputGain,
                    InputDevice = recording.InputDevice,
                    MonitorOutputDevice = recording.MonitorOutputDevice,
                    Segments = recording.Segments.Select(segment => new UtaPerformanceRecordingSegment
                    {
                        FileStartFrame = segment.FileStartFrame,
                        FrameCount = segment.FrameCount,
                        SongStartTimeMicroseconds = segment.SongStartTimeMicroseconds,
                        PlaybackRateMillionths = segment.PlaybackRateMillionths,
                        TimelineEpoch = segment.TimelineEpoch,
                        Reason = segment.Reason.ToString(),
                    }).ToArray(),
                    StartSongTimeMicroseconds = recording.Segments.Count > 0
                        ? recording.Segments[0].SongStartTimeMicroseconds
                        : 0,
                };
            }

            IReadOnlyList<UtaPerformancePitchFrame> replayFrames = scoring.ReplayFrames;
            var writer = new UtaPerformanceArchiveWriter(resolvePerformanceRoot());
            if (hasRecording)
            {
                await using var input = new FileStream(path!, FileMode.Open, FileAccess.Read, FileShare.Read);
                await writer.WriteAsync(new UtaPerformanceWriteRequest(
                    manifest,
                    replayFrames,
                    input)).ConfigureAwait(false);

                try
                {
                    Directory.Delete(Path.GetDirectoryName(path!)!, true);
                }
                catch
                {
                }
            }
            else
            {
                await writer.WriteAsync(new UtaPerformanceWriteRequest(
                    manifest,
                    replayFrames)).ConfigureAwait(false);
            }

            performanceArchived = true;
        }
        catch (Exception ex)
        {
            Logger.Log($"Uta recording archive finalisation failed: {ex.GetBaseException().Message}", level: LogLevel.Error);
        }
        finally
        {
            archiveFinalising = false;
        }
    }

    private string resolvePerformanceRoot()
    {
        if (!string.IsNullOrWhiteSpace(performanceRoot.Value))
            return Path.GetFullPath(performanceRoot.Value);

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "osu",
            "uta-performances");
    }

    private void onProgress(UtaRecordingProgress progress)
    {
        Progress = progress;
        ProgressChanged?.Invoke(progress);
    }

    protected override void Dispose(bool isDisposing)
    {
        if (gameplayClock != null)
            gameplayClock.OnSeek -= onSeek;

        session.ProgressChanged -= onProgress;

        // A take that was actively recording (player quit/failed out before the
        // natural-end watcher in Update() ever ran) would otherwise leave its WAV
        // finalised on disk but stranded under staging/: the writer closes a valid
        // file, but no performance.json is ever produced, so nothing surfaces it.
        // Route through the same archival path Update() uses so an early exit still
        // produces a browsable performance instead of an orphaned staging folder.
        if (!performanceArchived && session.State is UtaRecordingState.Recording or UtaRecordingState.Paused)
            _ = finaliseAndArchiveAsync();
        else
            _ = session.DisposeAsync();

        performanceRoot.UnbindAll();
        microphoneLatency.UnbindAll();
        inputGain.UnbindAll();
        keyShift.UnbindAll();
        microphoneDevice.UnbindAll();
        microphoneOutput.UnbindAll();
        base.Dispose(isDisposing);
    }
}
