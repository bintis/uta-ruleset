// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENSE file in the repository root for full licence text.

using System.Linq;

namespace osu.Game.Rulesets.Uta.Scoring;

public static class UtaAnalysisReportGenerator
{
    public static UtaAnalysisReport Generate(UtaPerformanceScore score)
    {
        if (score.MaximumUnits <= 0)
            return new UtaAnalysisReport(UtaAnalysisMessage.None, UtaAnalysisMessage.None);

        UtaAnalysisMessage positive = score.PitchAccuracyPermille >= 900 && score.StabilityPermille >= 800
            ? UtaAnalysisMessage.AccurateAndStable
            : score.PitchAccuracyPermille >= 900
                ? UtaAnalysisMessage.AccuratePitch
                : score.VibratoQualityPermille >= 800
                    ? UtaAnalysisMessage.ControlledVibrato
                    : score.LongToneQualityPermille >= 800
                        ? UtaAnalysisMessage.StrongLongTones
                        : score.CoveragePermille >= 900
                            ? UtaAnalysisMessage.ConsistentVoicing
                            : UtaAnalysisMessage.None;

        UtaAnalysisMessage advice;
        if (score.CoveragePermille < 700)
            advice = UtaAnalysisMessage.ImproveCoverage;
        else if (score.BiasCents > 35)
            advice = UtaAnalysisMessage.LowerPitch;
        else if (score.BiasCents < -35)
            advice = UtaAnalysisMessage.RaisePitch;
        else if (score.StabilityPermille < 550)
            advice = UtaAnalysisMessage.ImproveStability;
        else if (score.PitchAccuracyPermille < 800)
            advice = score.Notes.Count(note => note.Grade == UtaNoteGrade.Bad) >= 3
                ? UtaAnalysisMessage.ReduceBadNotes
                : UtaAnalysisMessage.ImprovePitchAccuracy;
        else
            advice = UtaAnalysisMessage.None;

        return new UtaAnalysisReport(positive, advice);
    }
}
