// Copyright (c) andy840119 <andy840119@gmail.com>. Licensed under the GPL Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Karaoke.Beatmaps;
using osu.Game.Rulesets.Karaoke.Configuration;
using osu.Game.Rulesets.Karaoke.Objects;
using osu.Game.Rulesets.Karaoke.Objects.Drawables;
using osu.Game.Rulesets.Karaoke.Mods;
using osu.Game.Rulesets.Karaoke.UI.Scrolling;
using osu.Game.Rulesets.Karaoke.UI.Uta;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Objects.Drawables;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.UI.Scrolling;
using osuTK;

namespace osu.Game.Rulesets.Karaoke.UI;

public partial class KaraokePlayfield : ScrollingPlayfield
{
    [Resolved]
    private IBindable<WorkingBeatmap> beatmap { get; set; } = null!;

    public WorkingBeatmap WorkingBeatmap => beatmap.Value;

    public LyricPlayfield LyricPlayfield { get; }

    public ScrollingNotePlayfield NotePlayfield { get; }

    public UtaLyricsDisplay UtaLyricsDisplay { get; }

    public UtaPitchGuide UtaPitchGuide { get; }

    public BindableBool DisplayCursor { get; set; } = new();
    public override bool ReceivePositionalInputAt(Vector2 screenSpacePos) => !DisplayCursor.Value && base.ReceivePositionalInputAt(screenSpacePos);

    private readonly BindableInt bindablePitch = new();
    private readonly BindableInt bindableVocalPitch = new();
    private readonly BindableInt bindablePlayback = new();
    private readonly BindableDouble notePlayfieldAlpha = new();
    private readonly BindableDouble lyricPlayfieldAlpha = new();
    private bool useUtaLyrics;
    private bool showUtaLyrics = true;
    private KaraokeBeatmap karaokeBeatmap = null!;

    public KaraokePlayfield()
    {
        AddInternal(LyricPlayfield = CreateLyricPlayfield().With(x =>
        {
            x.RelativeSizeAxes = Axes.Both;
        }));

        AddInternal(NotePlayfield = CreateNotePlayfield(9).With(x =>
        {
            x.RelativeSizeAxes = Axes.X;
        }));

        AddInternal(UtaLyricsDisplay = new UtaLyricsDisplay
        {
            Alpha = 0,
        });

        AddInternal(UtaPitchGuide = new UtaPitchGuide
        {
            Alpha = 0,
        });

        AddNested(LyricPlayfield);
        AddNested(NotePlayfield);

        bindablePitch.BindValueChanged(value =>
        {
            // Convert between -10 and 10 into 0.5 and 1.5
            float newValue = 1.0f + (float)value.NewValue / 40;
            WorkingBeatmap.Track.Frequency.Value = newValue;
        });

        bindableVocalPitch.BindValueChanged(value =>
        {
            // TODO : implement until has vocal track
        });

        bindablePlayback.BindValueChanged(value =>
        {
            // Convert between -10 and 10 into 0.5 and 1.5
            float newValue = 1.0f + (float)value.NewValue / 40;
            WorkingBeatmap.Track.Tempo.Value = newValue;
        });

        // Alpha
        notePlayfieldAlpha.BindValueChanged(x =>
        {
            // todo : how to check is there any notes in this playfield?
            double alpha = karaokeBeatmap.IsScorable() ? x.NewValue : 0;
            NotePlayfield.Alpha = (float)alpha;
        });
        lyricPlayfieldAlpha.BindValueChanged(x =>
        {
            LyricPlayfield.Alpha = useUtaLyrics ? 0 : (float)x.NewValue;
            UtaLyricsDisplay.Alpha = useUtaLyrics && showUtaLyrics ? (float)x.NewValue : 0;
        });
    }

    protected virtual LyricPlayfield CreateLyricPlayfield() => new();

    protected virtual ScrollingNotePlayfield CreateNotePlayfield(int columns) => new NotePlayfield(columns);

    #region Pooling support

    public override void Add(HitObject hitObject)
    {
        switch (hitObject)
        {
            case Lyric:
                LyricPlayfield.Add(hitObject);
                break;

            case Note:
            case BarLine:
                NotePlayfield.Add(hitObject);

                break;

            default:
                throw new ArgumentException($"Unsupported {nameof(HitObject)} type: {hitObject.GetType()}");
        }
    }

    public override bool Remove(HitObject hitObject)
    {
        switch (hitObject)
        {
            case Lyric:
                return LyricPlayfield.Remove(hitObject);

            case Note:
            case BarLine:
                return NotePlayfield.Remove(hitObject);

            default:
                throw new ArgumentException($"Unsupported {nameof(HitObject)} type: {hitObject.GetType()}");
        }
    }

    #endregion

    #region Non-pooling support

    public override void Add(DrawableHitObject h)
    {
        switch (h)
        {
            case DrawableLyric:
                LyricPlayfield.Add(h);
                break;

            case DrawableNote:
                NotePlayfield.Add(h);

                break;

            default:
                base.Add(h);
                break;
        }
    }

    public override bool Remove(DrawableHitObject h) =>
        h switch
        {
            DrawableLyric => LyricPlayfield.Remove(h),
            DrawableNote => NotePlayfield.Remove(h),
            _ => base.Remove(h),
        };

    #endregion

    public override void PostProcess()
    {
        base.PostProcess();

        // trigger again to update note playfield alpha.
        notePlayfieldAlpha.TriggerChange();
    }

    protected override void UpdateAfterChildren()
    {
        base.UpdateAfterChildren();

        // Legacy stage commands animate the lyric playfield's alpha after the Uta
        // layer has loaded. Uta owns lyric rendering, so keep the legacy layer off
        // after all child transforms have run to avoid displaying two lyric systems.
        if (useUtaLyrics)
        {
            LyricPlayfield.Alpha = 0;
            NotePlayfield.Alpha = 0;
        }
    }

    [BackgroundDependencyLoader]
    private void load(KaraokeRulesetConfigManager rulesetConfig, KaraokeSessionStatics session, KaraokeBeatmap convertedBeatmap, IBindable<IReadOnlyList<Mod>> mods)
    {
        karaokeBeatmap = convertedBeatmap;
        // Cursor
        rulesetConfig.BindWith(KaraokeRulesetSetting.ShowCursor, DisplayCursor);

        // Alpha
        rulesetConfig.BindWith(KaraokeRulesetSetting.NoteAlpha, notePlayfieldAlpha);
        rulesetConfig.BindWith(KaraokeRulesetSetting.LyricAlpha, lyricPlayfieldAlpha);

        // Pitch
        session.BindWith(KaraokeRulesetSession.Pitch, bindablePitch);
        session.BindWith(KaraokeRulesetSession.VocalPitch, bindableVocalPitch);
        session.BindWith(KaraokeRulesetSession.PlaybackSpeed, bindablePlayback);

        if (karaokeBeatmap is { UtaPackageId: not null } utaBeatmap)
        {
            useUtaLyrics = true;
            showUtaLyrics = mods.Value.All(mod => mod is not KaraokeModHideLyrics);
            UtaLyricsDisplay.SetSegments(utaBeatmap.UtaTranscriptSegments);
            LyricPlayfield.Hide();
            // Keep legacy hit objects alive for native scoring, but let the Uta guide
            // own the visual layout and timing window.
            NotePlayfield.Alpha = 0;
            if (mods.Value.Any(mod => mod is KaraokeModHidePitchGuide))
                UtaPitchGuide.Hide();
            else
                UtaPitchGuide.Show();
        }
        else
        {
            UtaLyricsDisplay.Hide();
        }

        lyricPlayfieldAlpha.TriggerChange();
    }
}
