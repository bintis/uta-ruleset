// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENSE file in the repository root for full licence text.

using System;
using osu.Game.Rulesets.Uta.Core;

namespace osu.Game.Rulesets.Uta.Recording;

/// <summary>
/// Shared-clock historical take mixer. The native results statistic panel may
/// create this controller and feed it the take plus packaged BGM/vocal assets.
/// </summary>
internal sealed class UtaTakePlaybackController : IDisposable
{
    private readonly UtaAudioRouter router;
    private UtaRoutedAudioStream? backgroundMusic;
    private UtaRoutedAudioStream? playerTake;
    private UtaRoutedAudioStream? originalVocal;
    private readonly UtaTakeComparisonState comparison = new();

    public UtaTakePlaybackController(UtaAudioRouter router)
    {
        this.router = router;
    }

    public void LoadBackgroundMusic(byte[] data, string? outputDevice)
    {
        backgroundMusic?.Dispose();
        backgroundMusic = router.CreateTrack(data, outputDevice);
    }

    public void LoadOriginalVocal(byte[] data, string? outputDevice)
    {
        originalVocal?.Dispose();
        originalVocal = router.CreateTrack(data, outputDevice);
    }

    public void LoadPlayerTake(string path, string? outputDevice)
    {
        playerTake?.Dispose();
        playerTake = router.CreateTrack(path, outputDevice);
    }

    public void Seek(double songTimeMilliseconds)
    {
        comparison.Seek(Math.Max(0, songTimeMilliseconds));
        backgroundMusic?.Seek(songTimeMilliseconds);
        playerTake?.Seek(songTimeMilliseconds);
        originalVocal?.Seek(songTimeMilliseconds);
    }

    public void SetRate(double playbackRate, double recordedTakeRate)
    {
        if (!double.IsFinite(playbackRate) || playbackRate <= 0)
            throw new ArgumentOutOfRangeException(nameof(playbackRate));
        if (!double.IsFinite(recordedTakeRate) || recordedTakeRate <= 0)
            throw new ArgumentOutOfRangeException(nameof(recordedTakeRate));

        applyMixRates(playbackRate, recordedTakeRate, lastSemitones);
    }

    private int lastSemitones;

    public void SetTranspose(int semitones)
    {
        lastSemitones = semitones;
        applyMixRates(lastPlaybackRate, lastRecordedTakeRate, semitones);
    }

    private double lastPlaybackRate = 1;
    private double lastRecordedTakeRate = 1;

    private void applyMixRates(double playbackRate, double recordedTakeRate, int semitones)
    {
        lastPlaybackRate = playbackRate;
        lastRecordedTakeRate = recordedTakeRate;
        lastSemitones = semitones;
        (double frequency, double tempo) = UtaAudioMath.TransposeFactors(semitones);
        backgroundMusic?.SetFrequency(frequency);
        backgroundMusic?.SetTempo(playbackRate * tempo);
        originalVocal?.SetFrequency(frequency);
        originalVocal?.SetTempo(playbackRate * tempo);
        playerTake?.SetFrequency(1);
        playerTake?.SetTempo(playbackRate / recordedTakeRate);
    }

    public void Select(UtaComparisonSide side)
    {
        comparison.Select(side);
        applyMix();
    }

    public void Toggle()
    {
        comparison.Toggle();
        applyMix();
    }

    public void Start()
    {
        comparison.SetPlaying(true);
        backgroundMusic?.Start();
        playerTake?.Start();
        originalVocal?.Start();
        applyMix();
    }

    public void Stop()
    {
        comparison.SetPlaying(false);
        backgroundMusic?.Stop();
        playerTake?.Stop();
        originalVocal?.Stop();
    }

    private void applyMix()
    {
        UtaComparisonMix mix = comparison.GetMix(1);
        backgroundMusic?.SetVolume((float)mix.BackgroundMusic);
        playerTake?.SetVolume((float)mix.PlayerTake);
        originalVocal?.SetVolume((float)mix.OriginalVocal);
    }

    public void Dispose()
    {
        backgroundMusic?.Dispose();
        playerTake?.Dispose();
        originalVocal?.Dispose();
    }
}
