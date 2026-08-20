// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENSE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Configuration;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Platform;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osu.Game.Graphics.UserInterface;
using osu.Game.Rulesets.Uta.Localisation;
using osu.Game.Rulesets.Uta.Performance;
using osu.Game.Rulesets.Uta.Scoring;
using osu.Game.Scoring;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.Uta.UI;

/// <summary>
/// Results-screen reader for the independent Uta performance archive. Native
/// ScoreInfo remains useful when the archive is missing; this panel adds saved
/// vocal detail, pitch replay and recording entry points.
/// </summary>
public sealed partial class UtaHistoricalPerformancePanel : CompositeDrawable
{
    private readonly ScoreInfo score;
    private readonly OsuSpriteText titleText;
    private readonly OsuSpriteText statusText;
    private readonly OsuSpriteText summaryText;
    private readonly OsuSpriteText detailText;
    private readonly OsuSpriteText replayStatusText;
    private readonly TextButton replayButton;
    private readonly TextButton recordingButton;
    private readonly TextButton folderButton;
    private readonly Bindable<string> locale = new();
    private UtaUiLanguage language;

    private GameHost host = null!;
    private UtaPerformanceArchiveEntry? archiveEntry;
    private IReadOnlyList<UtaPerformancePitchFrame> replayFrames = Array.Empty<UtaPerformancePitchFrame>();
    private bool replayPlaying;
    private int replayIndex;
    private double replayDelayMicroseconds;

    public UtaHistoricalPerformancePanel(ScoreInfo score)
    {
        this.score = score ?? throw new ArgumentNullException(nameof(score));
        RelativeSizeAxes = Axes.X;
        Height = 310;
        Masking = true;
        CornerRadius = 10;

        InternalChildren = new Drawable[]
        {
            new Box
            {
                RelativeSizeAxes = Axes.Both,
                Colour = new Color4(13, 15, 26, 245),
            },
            new FillFlowContainer
            {
                RelativeSizeAxes = Axes.X,
                AutoSizeAxes = Axes.Y,
                Padding = new MarginPadding(18),
                Spacing = new Vector2(0, 8),
                Direction = FillDirection.Vertical,
                Children = new Drawable[]
                {
                    titleText = new OsuSpriteText
                    {
                        Font = OsuFont.Default.With(size: 20, weight: FontWeight.Bold),
                    },
                    statusText = body(13, 0.72f),
                    summaryText = body(15, 1),
                    detailText = body(12, 0.86f),
                    replayStatusText = body(12, 0.72f),
                    new FillFlowContainer
                    {
                        RelativeSizeAxes = Axes.X,
                        AutoSizeAxes = Axes.Y,
                        Direction = FillDirection.Horizontal,
                        Spacing = new Vector2(8, 0),
                        Children = new Drawable[]
                        {
                            replayButton = new TextButton { Width = 165 },
                            recordingButton = new TextButton { Width = 145 },
                            folderButton = new TextButton { Width = 135 },
                        },
                    },
                },
            },
        };

        statusText.Text = string.Empty;
        replayStatusText.Text = string.Empty;
        replayButton.Enabled.Value = false;
        recordingButton.Enabled.Value = false;
        folderButton.Enabled.Value = false;
        replayButton.Action = toggleReplay;
        recordingButton.Action = openRecording;
        folderButton.Action = openArchiveFolder;
    }

    [BackgroundDependencyLoader]
    private void load(GameHost host, FrameworkConfigManager frameworkConfig)
    {
        this.host = host;
        locale.BindTo(frameworkConfig.GetBindable<string>(FrameworkSetting.Locale));
        locale.BindValueChanged(value =>
        {
            language = UtaLanguageResolver.FromLocale(value.NewValue);
            refreshLabels();
        }, true);
    }

    protected override void LoadComplete()
    {
        base.LoadComplete();
        loadArchive();
    }

    protected override void Update()
    {
        base.Update();
        if (!replayPlaying || replayFrames.Count == 0)
            return;

        replayDelayMicroseconds -= Time.Elapsed * 1000;
        while (replayPlaying && replayDelayMicroseconds <= 0)
        {
            UtaPerformancePitchFrame frame = replayFrames[replayIndex];
            showReplayFrame(frame);
            replayIndex++;
            if (replayIndex >= replayFrames.Count)
            {
                replayPlaying = false;
                replayButton.Text = UtaStrings.Get("archive.replay_again", language);
                replayStatusText.Text += " · complete";
                break;
            }

            replayDelayMicroseconds += delayBetween(frame, replayFrames[replayIndex]);
        }
    }

    private async void loadArchive()
    {
        try
        {
            LoadResult result = await Task.Run(async () =>
            {
                string root = UtaPerformanceRootResolver.Resolve(host);
                var library = new UtaPerformanceLibrary(root);
                await library.RefreshAsync().ConfigureAwait(false);
                UtaPerformanceArchiveEntry? entry = library.FindByLazerScoreId(score.ID) ?? findFallback(library);
                IReadOnlyList<UtaPerformancePitchFrame> frames = entry == null
                    ? Array.Empty<UtaPerformancePitchFrame>()
                    : await new UtaPerformanceArchiveReader().ReadPitchReplayAsync(entry.DirectoryPath).ConfigureAwait(false);
                return new LoadResult(root, entry, frames);
            }).ConfigureAwait(false);

            Schedule(() => showResult(result));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException)
        {
            Schedule(() =>
            {
                statusText.Text = $"Performance archive unavailable: {ex.GetBaseException().Message}";
                summaryText.Text = UtaStrings.Get("archive.native_score_available", language);
            });
        }
    }

    private UtaPerformanceArchiveEntry? findFallback(UtaPerformanceLibrary library)
    {
        IEnumerable<UtaPerformanceArchiveEntry> candidates = library.Entries.Where(entry =>
            UtaScoreProcessor.ToDisplayScore(entry.Manifest.Scoring.TotalScore) == score.TotalScore
            && string.Equals(entry.Manifest.Song.BeatmapHash, score.BeatmapHash, StringComparison.Ordinal));

        if (score.Date == default)
            return candidates.OrderByDescending(entry => entry.Manifest.CreatedAtUtc).FirstOrDefault();

        return candidates.OrderBy(entry => Math.Abs((entry.Manifest.CreatedAtUtc - score.Date).TotalSeconds))
                         .FirstOrDefault(entry => Math.Abs((entry.Manifest.CreatedAtUtc - score.Date).TotalMinutes) <= 5);
    }

    private void showResult(LoadResult result)
    {
        UtaPerformanceArchiveEntry? entry = result.Entry;
        archiveEntry = entry;
        replayFrames = result.Frames;
        if (entry == null)
        {
            statusText.Text = $"No detail archive found in {result.RootDirectory}";
            summaryText.Text = "Native score history is intact; only detailed vocal replay is unavailable.";
            folderButton.Enabled.Value = Directory.Exists(result.RootDirectory);
            folderButton.Action = () => host.OpenFileExternally(result.RootDirectory);
            return;
        }

        UtaPerformanceManifest manifest = entry.Manifest;
        statusText.Text = manifest.Eligibility.Comparable
            ? $"Saved {manifest.CreatedAtUtc.LocalDateTime:g} · comparable"
            : $"Saved {manifest.CreatedAtUtc.LocalDateTime:g} · practice/non-comparable";
        long displayScore = UtaScoreProcessor.ToDisplayScore(manifest.Scoring.TotalScore);
        long displayBaseScore = UtaScoreProcessor.ToDisplayScore(manifest.Scoring.TotalScoreWithoutMods);
        string baseScore = displayBaseScore > 0 && displayBaseScore != displayScore
            ? $" (base {displayBaseScore:N0})"
            : string.Empty;
        summaryText.Text = $"Score {displayScore:N0}{baseScore}   综合 {manifest.Scoring.CompositeRatingPermille / 10.0:0.0}%   音程 {manifest.Scoring.PitchAccuracyPermille / 10.0:0.0}%   稳定 {manifest.Scoring.StabilityPermille / 10.0:0.0}%";
        detailText.Text = $"Perfect {manifest.Judgements.Perfect} · Great {manifest.Judgements.Great} · Good {manifest.Judgements.Good} · Bad {manifest.Judgements.Bad} · Miss {manifest.Judgements.Miss} · High {manifest.Judgements.High} · Low {manifest.Judgements.Low} · Unstable {manifest.Judgements.Unstable}";
        if (manifest.Expression is { } expression && expression.Available)
            detailText.Text += $"\nRMS dynamic range {expression.DynamicRangeDecibelsTenths / 10.0:0.0} dB";

        replayButton.Enabled.Value = replayFrames.Count > 0;
        replayStatusText.Text = replayFrames.Count > 0
            ? $"Pitch replay loaded: {replayFrames.Count:N0} frames."
            : UtaStrings.Get("archive.no_replay", language);
        recordingButton.Enabled.Value = manifest.Files.Recording != null;
        folderButton.Enabled.Value = true;
    }

    private void toggleReplay()
    {
        if (replayFrames.Count == 0)
            return;

        if (replayPlaying)
        {
            replayPlaying = false;
            replayButton.Text = UtaStrings.Get("archive.resume_replay", language);
            return;
        }

        if (replayIndex >= replayFrames.Count)
            replayIndex = 0;
        replayPlaying = true;
        replayDelayMicroseconds = 0;
        replayButton.Text = UtaStrings.Get("archive.pause_replay", language);
    }

    private void showReplayFrame(UtaPerformancePitchFrame frame)
    {
        string pitch = frame.Voiced ? $"MIDI {frame.PitchCents / 100.0:0.00}" : "unvoiced";
        replayStatusText.Text = $"{frame.TimeMicroseconds / 1_000_000.0:0.000}s · epoch {frame.TimelineEpoch} · {pitch} · clarity {frame.ClarityPermille / 10.0:0.0}%";
    }

    private static long delayBetween(UtaPerformancePitchFrame current, UtaPerformancePitchFrame next)
    {
        if (current.TimelineEpoch != next.TimelineEpoch)
            return 250_000;
        return Math.Clamp(next.TimeMicroseconds - current.TimeMicroseconds, 1_000, 250_000);
    }

    private void openRecording()
    {
        UtaPerformanceArchiveEntry? entry = archiveEntry;
        if (entry == null || entry.Manifest.Files.Recording is not { } recording)
            return;

        host.OpenFileExternally(UtaPerformancePaths.ResolveContainedFile(entry.DirectoryPath, recording));
    }

    private void openArchiveFolder()
    {
        UtaPerformanceArchiveEntry? entry = archiveEntry;
        if (entry != null)
            host.OpenFileExternally(entry.DirectoryPath);
    }

    private void refreshLabels()
    {
        titleText.Text = UtaStrings.Get("archive.title", language);
        recordingButton.Text = UtaStrings.Get("archive.open_recording", language);
        folderButton.Text = UtaStrings.Get("archive.open", language);
        if (!replayPlaying)
            replayButton.Text = UtaStrings.Get(replayIndex >= replayFrames.Count && replayFrames.Count > 0 ? "archive.replay_again" : "archive.play_replay", language);
        if (string.IsNullOrEmpty(statusText.Text.ToString()))
            statusText.Text = UtaStrings.Get("archive.searching", language);
        if (string.IsNullOrEmpty(replayStatusText.Text.ToString()))
            replayStatusText.Text = UtaStrings.Get("archive.replay_not_loaded", language);
    }

    protected override void Dispose(bool isDisposing)
    {
        locale.UnbindAll();
        base.Dispose(isDisposing);
    }

    private static OsuSpriteText body(float size, float alpha)
        => new()
        {
            Font = OsuFont.Default.With(size: size),
            Colour = Color4.White,
            Alpha = alpha,
        };

    private sealed partial class TextButton : OsuButton
    {
    }

    private sealed record LoadResult(
        string RootDirectory,
        UtaPerformanceArchiveEntry? Entry,
        IReadOnlyList<UtaPerformancePitchFrame> Frames);
}
