// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.Linq;
using osu.Game.Rulesets.Scoring;
using osu.Game.Scoring;

namespace osu.Game.Rulesets.Karaoke.Scoring.Uta;

public sealed class UtaPitchScoring
{
    private const double good_pitch_semitones = 0.75;
    private const double exact_pitch_semitones = 0.35;

    private readonly Dictionary<int, PhraseAccumulator> phrases = new();
    private readonly Dictionary<int, NoteAccumulator> notes = new();

    private double earned;
    private double lastTime;
    private double referenceSeconds;
    private double voicedSeconds;
    private double stabilityEarned;
    private double? previousError;
    private int? previousNoteIndex;

    public void Reset()
    {
        earned = 0;
        lastTime = 0;
        referenceSeconds = 0;
        voicedSeconds = 0;
        stabilityEarned = 0;
        previousError = null;
        previousNoteIndex = null;
        phrases.Clear();
        notes.Clear();
    }

    public void Discontinuity(double currentTime)
    {
        lastTime = currentTime;
        previousError = null;
        previousNoteIndex = null;
    }

    public void AddFrame(
        double currentTime,
        double? referenceHertz,
        double? userHertz,
        double similarity,
        int? segmentIndex,
        int? noteIndex,
        int? noteMidi,
        bool allowOctaveTolerance)
    {
        double delta = Math.Clamp(currentTime - lastTime, 0, 0.1);
        lastTime = currentTime;

        userHertz = userHertz is { } hertz && UtaPitchMath.IsFinitePitch(hertz) ? hertz : null;
        double? noteTarget = noteMidi is { } midi ? UtaPitchMath.MidiToFrequency(midi) : null;
        double effectiveSimilarity = noteTarget is { } target && userHertz is { } user
            ? UtaPitchMath.Similarity(target, user, allowOctaveTolerance)
            : similarity;

        if (segmentIndex is { } phraseIndex)
        {
            var phrase = getPhrase(phraseIndex);
            if (userHertz != null && phrase.FirstVoiceTime == null)
                phrase.FirstVoiceTime = currentTime;

            if (referenceHertz != null)
            {
                phrase.ReferenceSeconds += delta;
                phrase.FirstReferenceTime ??= currentTime;
                if (userHertz != null)
                {
                    phrase.Earned += effectiveSimilarity * delta;
                    phrase.VoicedSeconds += delta;
                }
            }
        }

        if (referenceHertz == null)
        {
            previousError = null;
            previousNoteIndex = null;
            return;
        }

        referenceSeconds += delta;
        if (noteIndex is { } index)
            getNote(index).ReferenceSeconds += delta;

        if (userHertz == null)
        {
            previousError = null;
            previousNoteIndex = null;
            return;
        }

        earned += effectiveSimilarity * delta;
        voicedSeconds += delta;

        double referenceMidi = noteMidi ?? UtaPitchMath.FrequencyToMidi(referenceHertz.Value);
        double error = UtaPitchMath.Deviation(referenceMidi, UtaPitchMath.FrequencyToMidi(userHertz.Value), allowOctaveTolerance);
        bool sameNote = noteIndex == previousNoteIndex;
        double movement = sameNote ? Math.Abs(error - previousError.GetValueOrDefault(error)) : 0;
        double stability = Math.Exp(-Math.Pow(movement / 0.48, 2)) * Math.Min(effectiveSimilarity / 0.7, 1);

        stabilityEarned += stability * delta;
        previousError = error;
        previousNoteIndex = noteIndex;

        if (segmentIndex is { } currentPhrase)
            getPhrase(currentPhrase).StabilityEarned += stability * delta;

        if (noteIndex is { } currentNote)
        {
            var note = getNote(currentNote);
            note.VoicedSeconds += delta;
            note.Earned += effectiveSimilarity * delta;
            note.StabilityEarned += stability * delta;
            note.DeviationSeconds += error * delta;
            if (Math.Abs(error) <= good_pitch_semitones)
                note.HitSeconds += delta;
            if (Math.Abs(error) <= exact_pitch_semitones)
                note.ExactSeconds += delta;
        }
    }

    public IReadOnlyList<UtaNoteScore> GetNoteScores()
    {
        return notes.OrderBy(pair => pair.Key)
                    .Where(pair => pair.Value.ReferenceSeconds >= 0.04)
                    .Select(pair => createNoteScore(pair.Key, pair.Value))
                    .ToArray();
    }

    public UtaScoreSummary GetSummary()
    {
        double denominator = Math.Max(referenceSeconds, 0.001);
        double coverage = Math.Min(voicedSeconds / denominator, 1);
        double pitch = Math.Min(earned / denominator, 1);
        double stability = voicedSeconds > 0 ? Math.Min(stabilityEarned / voicedSeconds, 1) : 0;

        double timingQuality = 0;
        int timedCount = 0;
        foreach (var phrase in phrases.Values)
        {
            if (phrase.FirstReferenceTime is not { } reference || phrase.FirstVoiceTime is not { } voice)
                continue;

            double error = Math.Abs(voice - reference);
            timingQuality += error <= 0.12 ? 1 : Math.Max(0, 1 - (error - 0.12) / 0.48);
            timedCount++;
        }

        double timing = timedCount > 0 ? timingQuality / timedCount : 0;
        double longReference = 0;
        double longEarned = 0;

        foreach (var note in notes.Values.Where(note => note.ReferenceSeconds >= 0.55))
        {
            longReference += note.ReferenceSeconds;
            longEarned += note.Earned * 0.75 + note.StabilityEarned * 0.25;
        }

        double longTone = longReference > 0 ? Math.Min(longEarned / longReference, 1) : pitch;
        double accuracy = Math.Clamp(
            pitch * 0.62
            + stability * coverage * 0.13
            + coverage * 0.12
            + timing * coverage * 0.07
            + longTone * 0.06,
            0,
            1);

        return new UtaScoreSummary(
            accuracy,
            rankFromAccuracy(accuracy),
            percentage(pitch),
            percentage(stability),
            percentage(timing),
            percentage(coverage),
            percentage(longTone));
    }

    private static UtaNoteScore createNoteScore(int noteIndex, NoteAccumulator note)
    {
        double hitRatio = Math.Min(note.HitSeconds / note.ReferenceSeconds, 1);
        double exactRatio = Math.Min(note.ExactSeconds / note.ReferenceSeconds, 1);
        double accuracy = Math.Min(note.Earned / note.ReferenceSeconds, 1);
        double coverage = Math.Min(note.VoicedSeconds / note.ReferenceSeconds, 1);
        double deviation = note.VoicedSeconds > 0 ? note.DeviationSeconds / note.VoicedSeconds : 0;

        UtaNoteGrade grade = hitRatio >= 0.86 && accuracy >= 0.94
            ? UtaNoteGrade.Perfect
            : hitRatio >= 0.6
                ? UtaNoteGrade.Good
                : coverage < 0.35 || Math.Abs(deviation) < 0.18
                    ? UtaNoteGrade.Miss
                    : deviation > 0 ? UtaNoteGrade.High : UtaNoteGrade.Low;

        return new UtaNoteScore(noteIndex, hitRatio, exactRatio, accuracy, coverage, deviation, grade, hitResultFor(grade));
    }

    private PhraseAccumulator getPhrase(int index)
    {
        if (!phrases.TryGetValue(index, out var accumulator))
            phrases.Add(index, accumulator = new PhraseAccumulator());
        return accumulator;
    }

    private NoteAccumulator getNote(int index)
    {
        if (!notes.TryGetValue(index, out var accumulator))
            notes.Add(index, accumulator = new NoteAccumulator());
        return accumulator;
    }

    private static int percentage(double value) => (int)Math.Round(Math.Clamp(value, 0, 1) * 100);

    private static ScoreRank rankFromAccuracy(double accuracy) => accuracy switch
    {
        >= 0.95 => ScoreRank.S,
        >= 0.90 => ScoreRank.A,
        >= 0.80 => ScoreRank.B,
        >= 0.70 => ScoreRank.C,
        _ => ScoreRank.D,
    };

    private static HitResult hitResultFor(UtaNoteGrade grade) => grade switch
    {
        UtaNoteGrade.Perfect => HitResult.Perfect,
        UtaNoteGrade.Good => HitResult.Great,
        UtaNoteGrade.High or UtaNoteGrade.Low => HitResult.Meh,
        _ => HitResult.Miss,
    };

    private sealed class PhraseAccumulator
    {
        public double ReferenceSeconds;
        public double Earned;
        public double? FirstReferenceTime;
        public double? FirstVoiceTime;
        public double VoicedSeconds;
        public double StabilityEarned;
    }

    private sealed class NoteAccumulator
    {
        public double ReferenceSeconds;
        public double VoicedSeconds;
        public double Earned;
        public double StabilityEarned;
        public double HitSeconds;
        public double ExactSeconds;
        public double DeviationSeconds;
    }
}

public readonly record struct UtaScoreSummary(
    double Accuracy,
    ScoreRank Rank,
    int PitchAccuracy,
    int Stability,
    int Timing,
    int Coverage,
    int LongTone);

public readonly record struct UtaNoteScore(
    int NoteIndex,
    double HitRatio,
    double ExactRatio,
    double Accuracy,
    double Coverage,
    double Deviation,
    UtaNoteGrade Grade,
    HitResult HitResult);

public enum UtaNoteGrade
{
    Perfect,
    Good,
    High,
    Low,
    Miss,
}
