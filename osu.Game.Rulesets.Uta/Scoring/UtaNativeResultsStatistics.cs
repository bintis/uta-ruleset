// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENSE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Game.Beatmaps;
using osu.Game.Graphics.Sprites;
using osu.Game.Graphics.UserInterface;
using osu.Game.Rulesets.Uta.Core;
using osu.Game.Rulesets.Uta.Performance;
using osu.Game.Rulesets.Uta.Recording;
using osu.Game.Scoring;
using osu.Game.Screens.Ranking.Statistics;
using osuTK;

namespace osu.Game.Rulesets.Uta.Scoring;

/// <summary>
/// Adds Uta details to lazer's existing ranking screen. This is intentionally
/// a StatisticItem, not a second results screen/navigation flow.
/// </summary>
internal static class UtaNativeResultsStatistics
{
    public static StatisticItem[] Create(ScoreInfo score, IBeatmap playableBeatmap)
    {
        if (playableBeatmap is not UtaBeatmap uta)
            return Array.Empty<StatisticItem>();

        return new[]
        {
            new StatisticItem("Uta singing analysis", () => new UtaNativeResultsPanel(score, uta)
            {
                RelativeSizeAxes = Axes.X,
            }),
        };
    }
}

internal sealed partial class UtaNativeResultsPanel : CompositeDrawable
{
    private readonly ScoreInfo score;
    private readonly UtaBeatmap beatmap;
    private readonly FillFlowContainer<Drawable> content;
    private readonly OsuSpriteText status;
    private UtaPerformanceArchiveEntry? entry;
    private UtaTransposeRecommendation transposeRecommendation;
    private readonly UtaTakeComparisonState comparison = new();

    public UtaNativeResultsPanel(ScoreInfo score, UtaBeatmap beatmap)
    {
        this.score = score;
        this.beatmap = beatmap;
        AutoSizeAxes = Axes.Y;

        InternalChild = content = new FillFlowContainer<Drawable>
        {
            RelativeSizeAxes = Axes.X,
            AutoSizeAxes = Axes.Y,
            Direction = FillDirection.Vertical,
            Spacing = new Vector2(0, 6),
            Children = new Drawable[]
            {
                new OsuSpriteText
                {
                    Font = osu.Game.Graphics.OsuFont.GetFont(size: 24, weight: osu.Game.Graphics.FontWeight.Bold),
                    Text = $"{score.TotalScore:0} / {UtaScoreProcessor.DISPLAY_MAX_SCORE}",
                },
                new OsuSpriteText
                {
                    Text = $"Native result: {score.Rank} · {score.Accuracy:P2}",
                },
                status = new OsuSpriteText { Text = "Loading Uta performance archive…" },
            },
        };
    }

    protected override void LoadComplete()
    {
        base.LoadComplete();
        _ = Task.Run(loadArchiveAsync);
    }

    private async Task loadArchiveAsync()
    {
        try
        {
            string root = UtaPerformanceRootRegistry.Resolve();
            var library = new UtaPerformanceLibrary(root);
            await library.RefreshAsync().ConfigureAwait(false);

            entry = library.FindByLazerScoreId(score.ID);
            if (entry == null)
            {
                // First display after gameplay may precede the post-import score link.
                // Select only the newest archive for the exact package, then link it.
                entry = library.FindByPackageId(beatmap.PackageId).FirstOrDefault();
                if (entry != null && score.ID != Guid.Empty)
                {
                    try
                    {
                        await new UtaPerformanceScoreLinker().LinkAsync(entry.DirectoryPath, score.ID, null).ConfigureAwait(false);
                    }
                    catch
                    {
                        // The ranking page still displays the archive even if the
                        // best-effort ID patch cannot be persisted.
                    }
                }
            }

            if (entry != null)
            {
                try
                {
                    IReadOnlyList<UtaPerformancePitchFrame> replay = await new UtaPerformanceArchiveReader()
                        .ReadPitchReplayAsync(entry.DirectoryPath).ConfigureAwait(false);
                    var advisor = new UtaVocalRangeAdvisor();
                    foreach (UtaPerformancePitchFrame frame in replay)
                    {
                        if (frame.Voiced)
                            advisor.AddObservation(frame.PitchCents, frame.ClarityPermille);
                    }
                    transposeRecommendation = advisor.Recommend(UtaScoringBeatmapAdapter.CreateTargets(beatmap));
                }
                catch
                {
                    transposeRecommendation = default;
                }
            }

            Schedule(renderArchive);
        }
        catch (Exception ex)
        {
            Schedule(() => status.Text = $"Detailed archive unavailable: {ex.GetBaseException().Message}");
        }
    }

    private void renderArchive()
    {
        if (entry == null)
        {
            status.Text = "Detailed performance archive unavailable.";
            return;
        }

        UtaPerformanceManifest manifest = entry.Manifest;
        status.Text =
            $"Pitch {manifest.Scoring.PitchAccuracyPermille / 10.0:0.0}% · " +
            $"Coverage {manifest.Scoring.CoveragePermille / 10.0:0.0}% · " +
            $"Stability {manifest.Scoring.StabilityPermille / 10.0:0.0}% · " +
            $"Profile {manifest.Scoring.Profile}";

        if (transposeRecommendation.Available)
        {
            content.Add(new OsuSpriteText
            {
                Text = transposeRecommendation.Semitones == 0
                    ? "Vocal range: original key fits the observed range"
                    : $"Vocal range: try Transpose {transposeRecommendation.Semitones:+#;-#;0} next play",
            });
        }

        foreach (UtaPerformancePhraseSummary phrase in manifest.Phrases)
        {
            content.Add(new OsuSpriteText
            {
                Text =
                    $"Phrase {phrase.PhraseIndex + 1}: pitch {phrase.PitchAccuracyPermille / 10.0:0.0}% · " +
                    $"coverage {phrase.CoveragePermille / 10.0:0.0}% · " +
                    $"stability {phrase.StabilityPermille / 10.0:0.0}% · " +
                    $"bias {phrase.BiasCents:+#;-#;0} cents · misses {phrase.MissedIntervals.Count}",
            });
        }

        if (manifest.Files.Recording == null)
            return;

        string recordingPath = Path.Combine(entry.DirectoryPath, manifest.Files.Recording);
        content.Add(new OsuSpriteText
        {
            Text = $"Recorded take: {manifest.Files.Recording}",
        });

        var comparisonButton = new TextButton
        {
            RelativeSizeAxes = Axes.X,
            Height = 34,
            Text = "A/B: player take",
        };
        comparisonButton.Action = () =>
        {
            comparison.Toggle();
            comparisonButton.Text = comparison.Side == UtaComparisonSide.PlayerTake
                ? "A/B: player take"
                : "A/B: packaged vocal";
        };
        content.Add(comparisonButton);

        content.Add(new TextButton
        {
            RelativeSizeAxes = Axes.X,
            Height = 34,
            Text = "Open recording location",
            Action = () => openDirectory(entry.DirectoryPath),
        });

        content.Add(new TextButton
        {
            RelativeSizeAxes = Axes.X,
            Height = 34,
            Text = "Export WAV beside archive",
            Action = () =>
            {
                string exported = Path.Combine(
                    entry.DirectoryPath,
                    $"export-{manifest.PerformanceId:D}.wav");
                _ = UtaRecordingExportService.ExportCompleteAsync(recordingPath, exported);
            },
        });
    }

    private sealed partial class TextButton : OsuButton
    {
    }

    private static void openDirectory(string directory)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = directory,
                UseShellExecute = true,
            });
        }
        catch
        {
        }
    }
}
