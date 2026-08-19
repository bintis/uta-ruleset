// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENSE file in the repository root for full licence text.

using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using osu.Framework.Allocation;
using osu.Framework.Audio;
using osu.Framework.Extensions;
using osu.Framework.Logging;
using osu.Framework.Screens;
using osu.Framework.Testing;
using osu.Game.Beatmaps;
using osu.Game.Database;
using osu.Game.Rulesets;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.UI;
using osu.Game.Rulesets.Uta;
using osu.Game.Rulesets.Uta.Configuration;
using osu.Game.Rulesets.Uta.Core;
using osu.Game.Rulesets.Uta.Formats;
using osu.Game.Rulesets.Uta.Mods;
using osu.Game.Rulesets.Uta.Remote;
using osu.Game.Rulesets.Uta.UI;
using osu.Game.Screens.Play;
using osu.Game.Screens.Select;
using osu.Game.Tests.Visual;
using osuTK.Input;

namespace osu.Game.Rulesets.Uta.Tests;

/// <summary>
/// osu's own headless open-song path: <c>BeatmapManager.Import</c> then
/// <c>OsuGame.PresentBeatmap</c>, then framework <c>InputManager.Key(Enter)</c>.
/// Same contract as <c>TestScenePresentBeatmap</c> / <c>TestSceneSongSelectNavigation</c>.
/// </summary>
[TestFixture]
[Explicit("Interactive VisualTestRunner. Imports real UTZ via PresentBeatmap.")]
public partial class TestSceneUtaPresentBeatmap : OsuGameTestScene
{
    private static readonly string first_utz = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        "Documents", "uta!", "Asphodelos.utz");

    private static readonly string second_utz = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        "Desktop",
        "Another Infinity feat. Mayumi Morinaga (KONAMI MUSICフル) - Snow Crystal.utz");

    private BeatmapSetInfo? firstSet;
    private BeatmapSetInfo? secondSet;
    private WorkingBeatmap? firstPlayedBeatmap;
    private WorkingBeatmap? secondPlayedBeatmap;

    [Test]
    public void TestPresentPlaySwitchAndPreview()
    {
        AddAssert("first utz exists", () => File.Exists(first_utz), () => Is.True);
        AddAssert("second utz exists", () => File.Exists(second_utz), () => Is.True);
        AddStep("seed uta leftover settings", seedLeftoverSettings);

        AddStep("import first utz", () => firstSet = importUtz(first_utz));
        AddStep("import second utz", () => secondSet = importUtz(second_utz));
        AddAssert("both imported", () => firstSet != null && secondSet != null);

        present(() => firstSet!);
        AddStep("clear mods", () => Game.SelectedMods.Value = Array.Empty<Mod>());
        enterPlay();
        AddStep("capture play 1 track", () => firstPlayedBeatmap = ((Player)Game.ScreenStack.CurrentScreen).Beatmap.Value);
        AddAssert("play 1 has no VOX mod", () => Game.SelectedMods.Value.All(mod => mod is not UtaModOriginalVocals));
        AddAssert("play 1 original vocals off", () => !drawableUta().RuntimeModes.OriginalVocalsEnabled.Value);
        waitPastInitialSkip("play 1 ran past gap skip");

        exitToSongSelect();

        present(() => secondSet!);
        assertPreviewFollowsSelection();
        AddAssert("play 1 track is stopped", () => trackIsStopped(firstPlayedBeatmap));
        AddStep("enable VOX through song select", () =>
            ((SoloSongSelect)Game.ScreenStack.CurrentScreen).Mods.Value = new Mod[] { new UtaModOriginalVocals() });
        AddAssert("VOX preference captured", () => UtaRulesetRuntime.Instance.OriginalVocalsPreferred);
        enterPlay();
        AddStep("capture play 2 track", () => secondPlayedBeatmap = ((Player)Game.ScreenStack.CurrentScreen).Beatmap.Value);
        AddAssert("play 2 original vocals on", () => drawableUta().RuntimeModes.OriginalVocalsEnabled.Value);
        AddAssert("play 2 vocals graph exists", () => audioController().HasActiveVocals);
        waitPastInitialSkip("play 2 ran past gap skip");
        AddAssert("play 2 vocals advanced", () => audioController().VocalsPosition > 1000);

        exitToSongSelect();

        present(() => firstSet!);
        AddUntilStep(
            "preview is the presented chart",
            () => sameSet(Game.Beatmap.Value.BeatmapSetInfo, firstSet!)
                  && Game.Beatmap.Value.TrackLoaded);
        assertPreviewFollowsSelection();
        AddAssert("play 2 track is stopped", () => trackIsStopped(secondPlayedBeatmap));
    }

    private static bool trackIsStopped(WorkingBeatmap? beatmap)
    {
        if (beatmap == null || !beatmap.TrackLoaded)
            return true;

        return !beatmap.Track.IsRunning;
    }

    private BeatmapSetInfo importUtz(string path)
    {
        using FileStream input = File.OpenRead(path);
        var output = new MemoryStream();
        UtzBeatmapSetConverter.Convert(input, output);
        output.Position = 0;

        Live<BeatmapSetInfo>? imported = Game.BeatmapManager
            .Import(new ImportTask(output, Path.GetFileNameWithoutExtension(path) + ".osz"))
            .GetResultSafely();

        if (imported == null)
            throw new InvalidOperationException($"Import failed for '{path}'.");

        return imported.PerformRead(set => set.Detach());
    }

    private void seedLeftoverSettings()
    {
        UtaRulesetConfigManager? config;
        try
        {
            config = Game.Dependencies.Get<IRulesetConfigCache>().GetConfigFor(new UtaRuleset()) as UtaRulesetConfigManager;
        }
        catch (Exception exception)
        {
            Logger.Log($"Uta leftover test could not load ruleset config: {exception.Message}");
            return;
        }

        if (config == null)
        {
            Logger.Log("Uta leftover test ruleset config was null.");
            return;
        }

        config.SetValue(UtaRulesetSetting.DebugDiagnostics, true);
        config.SetValue(UtaRulesetSetting.AccompanimentLatency, -411f);

        string? output = UtaAudioDevices.Enumerate()
            .Select(device => device.Name)
            .FirstOrDefault(name => name.Contains("MARANTZ", StringComparison.OrdinalIgnoreCase))
            ?? UtaAudioDevices.Enumerate()
                .Select(device => device.Name)
                .FirstOrDefault(name => !UtaAudioDevices.IsPlaceholderOutput(name));

        if (!string.IsNullOrEmpty(output))
        {
            config.SetValue(UtaRulesetSetting.BackgroundMusicOutputDevice, output);
            config.SetValue(UtaRulesetSetting.OriginalVocalsOutputDevice, output);
            config.SetValue(UtaRulesetSetting.MicrophoneOutputDevice, output);
            Game.Dependencies.Get<AudioManager>().AudioDevice.Value = output;
        }

        string? microphone = UtaMicrophoneDevices.Enumerate()
            .Select(device => device.Name)
            .FirstOrDefault(name => name.Contains("AKG", StringComparison.OrdinalIgnoreCase))
            ?? UtaMicrophoneDevices.Enumerate().Select(device => device.Name).FirstOrDefault();

        if (!string.IsNullOrEmpty(microphone))
            config.SetValue(UtaRulesetSetting.MicrophoneDevice, microphone);

        Logger.Log($"Uta leftover test seeded output='{output}' mic='{microphone}' latency=-411");
    }

    private void present(Func<BeatmapSetInfo> getSet)
    {
        AddUntilStep("beatmap bindable writable", () => !Game.Beatmap.Disabled);
        AddStep("present beatmap", () => Game.PresentBeatmap(getSet()));
        AddUntilStep(
            "wait for song select",
            () => Game.ScreenStack.CurrentScreen is SoloSongSelect songSelect && songSelect.CarouselItemsPresented);
        AddUntilStep(
            "correct beatmap displayed",
            () => sameSet(Game.Beatmap.Value.BeatmapSetInfo, getSet())
                  || Game.Beatmap.Value.ToString().Contains(getSet().Metadata.Title, StringComparison.Ordinal));
    }

    private void assertPreviewFollowsSelection()
    {
        AddUntilStep(
            "selected preview is running",
            () => Game.Beatmap.Value.TrackLoaded && Game.MusicController.CurrentTrack.IsRunning);
        AddStep("log selected preview state", () => Logger.Log(
            $"Uta leftover test preview: selected='{Game.Beatmap.Value}' "
            + $"selectedLoaded={Game.Beatmap.Value.TrackLoaded} selectedRunning={Game.Beatmap.Value.Track.IsRunning} "
            + $"selectedTime={Game.Beatmap.Value.Track.CurrentTime:0.0} "
            + $"musicRunning={Game.MusicController.CurrentTrack.IsRunning} musicTime={Game.MusicController.CurrentTrack.CurrentTime:0.0}"));
    }

    private void enterPlay()
    {
        AddStep("press enter", () => InputManager.Key(Key.Enter));
        AddUntilStep("player loaded", () =>
        {
            DismissAnyNotifications();
            return Game.ScreenStack.CurrentScreen is Player player && player.IsLoaded;
        });
    }

    private void exitToSongSelect()
    {
        AddUntilStep("exit to song select", () =>
        {
            DismissAnyNotifications();
            if (Game.ScreenStack.CurrentScreen is SoloSongSelect)
                return true;

            Game.ScreenStack.CurrentScreen?.Exit();
            return false;
        });
        AddUntilStep("lease returned", () => !Game.Beatmap.Disabled);
    }

    private static bool sameSet(IBeatmapSetInfo current, BeatmapSetInfo expected)
    {
        if (current is BeatmapSetInfo typed
            && (typed.ID == expected.ID || (!string.IsNullOrEmpty(expected.Hash) && typed.Hash == expected.Hash)))
            return true;

        return current.Metadata.Title == expected.Metadata.Title
               && current.Metadata.Artist == expected.Metadata.Artist;
    }

    private void waitPastInitialSkip(string description)
    {
        AddUntilStep(
            description,
            () => Game.ChildrenOfType<GameplayClockContainer>().SingleOrDefault()?.CurrentTime >= 3000);
    }

    private DrawableUtaRuleset drawableUta()
        => Game.ChildrenOfType<DrawableUtaRuleset>().Single();

    private UtaAudioController audioController()
        => Game.ChildrenOfType<UtaAudioController>().Single();
}
