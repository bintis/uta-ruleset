// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENSE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;

namespace osu.Game.Rulesets.Uta.Scoring;

/// <summary>
/// A deterministic streaming wrapper around <see cref="UtaScoringEngine"/>.
/// The caller supplies mapped song-time frames and advances a capture-time
/// watermark. Notes are committed only after the configured delay, allowing
/// late pitch-analysis windows to arrive without depending on render timing.
/// </summary>
public sealed class UtaStreamingScoringSession
{
    private readonly UtaScoringTarget[] targets;
    private readonly UtaScoringOptions options;
    private readonly UtaScoringEngine engine;
    private readonly List<UtaScoringFrame> frames = new();
    private readonly Dictionary<int, UtaNoteScore> completed = new();
    private int nextTarget;
    private long lastWatermark = long.MinValue;

    public IReadOnlyDictionary<int, UtaNoteScore> CompletedNotes => completed;

    public UtaStreamingScoringSession(IEnumerable<UtaScoringTarget> targets, UtaScoringOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(targets);
        this.options = options ?? new UtaScoringOptions();
        this.options.Validate();
        UtaScoringTarget[] targetArray = targets.ToArray();
        engine = new UtaScoringEngine(this.options);
        engine.ValidateTargets(targetArray.OrderBy(target => target.StartTimeMicroseconds)
                                          .ThenBy(target => target.Index)
                                          .ToArray());
        this.targets = targetArray.OrderBy(target => target.EndTimeMicroseconds)
                                  .ThenBy(target => target.Index)
                                  .ToArray();
    }

    public void AddFrame(UtaScoringFrame frame)
    {
        if (frame.TimelineEpoch != options.TimelineEpoch)
            return;
        if (frame.TimeMicroseconds < 0)
            return; // Capture during gameplay lead-in is outside every scoring target.
        if (frame.ClarityPermille > UtaScoringOptions.QUALITY_SCALE)
            throw new ArgumentOutOfRangeException(nameof(frame));
        if (frame.Voiced && frame.PitchCents is < 0 or > 12_700)
            throw new ArgumentOutOfRangeException(nameof(frame));

        frames.Add(frame);
    }

    public IReadOnlyList<UtaNoteScore> AdvanceWatermark(long songTimeMicroseconds)
    {
        if (songTimeMicroseconds < lastWatermark)
            throw new ArgumentOutOfRangeException(nameof(songTimeMicroseconds), "A scoring-session watermark cannot move backwards.");
        lastWatermark = songTimeMicroseconds;

        var newlyCompleted = new List<UtaNoteScore>();
        while (nextTarget < targets.Length
               && targets[nextTarget].EndTimeMicroseconds + options.CommitDelayMicroseconds <= songTimeMicroseconds)
        {
            UtaScoringTarget target = targets[nextTarget++];
            UtaNoteScore score = engine.ScoreNote(target, frames);
            completed[target.Index] = score;
            newlyCompleted.Add(score);
        }

        return newlyCompleted;
    }

    public bool TryGetCompletedNote(int scoringIndex, out UtaNoteScore? score)
        => completed.TryGetValue(scoringIndex, out score);

    public UtaPerformanceScore CompletePerformance() => engine.Score(targets, frames);
}
