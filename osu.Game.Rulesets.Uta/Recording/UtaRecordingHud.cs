// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENSE file in the repository root for full licence text.

using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Sprites;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;

namespace osu.Game.Rulesets.Uta.Recording;

/// <summary>
/// Persistent, text-labelled recording indicator. Recording can never run
/// invisibly; colour is supplemental and not the only state signal.
/// </summary>
internal sealed partial class UtaRecordingHud : CompositeDrawable
{
    private readonly OsuSpriteText text;
    private UtaRecordingRuntime runtime = null!;

    public UtaRecordingHud()
    {
        Anchor = Anchor.TopLeft;
        Origin = Anchor.TopLeft;
        AutoSizeAxes = Axes.Both;
        Margin = new MarginPadding(16);
        InternalChild = text = new OsuSpriteText
        {
            Text = "REC off",
            Font = OsuFont.Default.With(size: 14, weight: FontWeight.Bold),
        };
    }

    [BackgroundDependencyLoader]
    private void load(UtaRecordingRuntime runtime)
    {
        this.runtime = runtime;
        runtime.ProgressChanged += onProgress;
        onProgress(runtime.Progress);
    }

    private void onProgress(UtaRecordingProgress progress)
    {
        Schedule(() =>
        {
            text.Text = progress.State switch
            {
                UtaRecordingState.Recording => $"● REC  {progress.RecordedFrames:N0} frames",
                UtaRecordingState.Paused => "REC paused",
                UtaRecordingState.Finalizing => "REC finalizing…",
                UtaRecordingState.Faulted => $"REC stopped: {progress.ErrorMessage}",
                UtaRecordingState.Completed => "REC saved",
                _ => "REC off",
            };
        });
    }

    protected override void Dispose(bool isDisposing)
    {
        if (runtime != null)
            runtime.ProgressChanged -= onProgress;
        base.Dispose(isDisposing);
    }
}
