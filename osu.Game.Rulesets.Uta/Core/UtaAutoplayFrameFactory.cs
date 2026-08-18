// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENSE file in the repository root for full licence text.

using osu.Game.Rulesets.Uta.Pitch;

namespace osu.Game.Rulesets.Uta.Core;

internal static class UtaAutoplayFrameFactory
{
    public const double FRAME_DURATION_MILLISECONDS = 20;

    public static UtaPitchFrame Create(UtaNote? activeNote, int semitoneShift, long arrivalTimestamp)
    {
        if (activeNote?.Midi is not { } midi)
            return new UtaPitchFrame(null, 0, 0, arrivalTimestamp, FRAME_DURATION_MILLISECONDS);

        double hertz = UtaPitchMath.MidiToFrequency(midi + semitoneShift);
        return new UtaPitchFrame(hertz, 1, 0.25, arrivalTimestamp, FRAME_DURATION_MILLISECONDS);
    }
}
