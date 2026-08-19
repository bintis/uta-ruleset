// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENSE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using osu.Game.Rulesets.Uta.Formats;

namespace osu.Game.Rulesets.Uta.UI.HUD.Lyrics;

/// <summary>
/// Ruleset-owned lyrics state. It evaluates the gameplay clock directly and only replaces its
/// progress buffer when the active segment changes.
/// </summary>
public sealed class UtaLyricsPresentationState
{
    private double[] wordProgress = Array.Empty<double>();
    private int? countdown;

    public IReadOnlyList<UtaTranscriptSegment> Segments { get; private set; } = Array.Empty<UtaTranscriptSegment>();

    public int SegmentIndex { get; private set; } = -1;

    public void SetSegments(IReadOnlyList<UtaTranscriptSegment> segments)
    {
        Segments = UtaLyricsTimeline.Normalize(segments);
        SegmentIndex = -1;
        countdown = null;
        wordProgress = Array.Empty<double>();
    }

    public UtaLyricsPresentationUpdate Update(double songTimeMilliseconds, double lyricsLatencyMilliseconds)
    {
        // Positive latency intentionally delays lyrics, preserving the pre-HUD migration sign.
        double seconds = (songTimeMilliseconds - lyricsLatencyMilliseconds) / 1000;
        int index = UtaLyricsTimeline.FindCurrentSegment(Segments, seconds, Math.Max(0, SegmentIndex));
        bool structuralChange = index != SegmentIndex;
        if (structuralChange)
        {
            SegmentIndex = index;
            wordProgress = index >= 0 && index < Segments.Count
                ? new double[Segments[index].Words.Count]
                : Array.Empty<double>();
        }

        UtaLyricsFrame frame = UtaLyricsTimeline.Evaluate(Segments, seconds, Math.Max(0, SegmentIndex), wordProgress);
        bool countdownChanged = frame.Countdown != countdown;
        countdown = frame.Countdown;
        return new UtaLyricsPresentationUpdate(frame, structuralChange, countdownChanged);
    }
}

public readonly record struct UtaLyricsPresentationUpdate(UtaLyricsFrame Frame, bool StructuralChange, bool CountdownChanged);
