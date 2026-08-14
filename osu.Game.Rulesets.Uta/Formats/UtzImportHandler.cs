// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENSE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using osu.Framework.Logging;
using osu.Game.Beatmaps;
using osu.Game.Database;
using osu.Game.Overlays;
using osu.Game.Overlays.Notifications;

namespace osu.Game.Rulesets.Uta.Formats;

/// <summary>
/// Registers UTZ with lazer's public file-import pipeline. UTZ validation and
/// conversion happen in memory, after which the native beatmap importer owns the
/// database, duplicate handling, progress notification and presentation flow.
/// </summary>
public sealed class UtzImportHandler : ICanAcceptFiles
{
    private static readonly ConditionalWeakTable<OsuGameBase, UtzImportHandler> registered_handlers = new();
    private static readonly object registration_lock = new();

    private readonly OsuGameBase game;
    private readonly BeatmapManager beatmapManager;
    private readonly INotificationOverlay? notifications;

    public IEnumerable<string> HandledExtensions => new[] { ".utz" };

    private UtzImportHandler(OsuGameBase game, BeatmapManager beatmapManager, INotificationOverlay? notifications)
    {
        this.game = game;
        this.beatmapManager = beatmapManager;
        this.notifications = notifications;
    }

    public static void EnsureRegistered(OsuGameBase? game, BeatmapManager? beatmapManager, INotificationOverlay? notifications)
    {
        if (game == null || beatmapManager == null)
            return;

        lock (registration_lock)
        {
            if (registered_handlers.TryGetValue(game, out _))
                return;

            var handler = new UtzImportHandler(game, beatmapManager, notifications);
            registered_handlers.Add(game, handler);
            game.RegisterImportHandler(handler);
            Logger.Log("Registered native .utz drag-and-drop import handler.");
        }
    }

    public Task Import(params string[] paths)
        => Import(paths.Select(path => new ImportTask(path)).ToArray());

    public async Task Import(ImportTask[] tasks, ImportParameters parameters = default)
    {
        var convertedTasks = new List<ImportTask>();

        foreach (var task in tasks)
        {
            var output = new MemoryStream();

            try
            {
                if (task.Stream != null)
                {
                    if (task.Stream.CanSeek)
                        task.Stream.Position = 0;

                    UtzBeatmapSetConverter.Convert(task.Stream, output);
                    task.Stream.Dispose();
                }
                else
                {
                    using Stream input = File.OpenRead(task.Path);
                    UtzBeatmapSetConverter.Convert(input, output);
                }

                output.Position = 0;
                string filename = Path.GetFileNameWithoutExtension(task.Path) + ".osz";
                convertedTasks.Add(new ImportTask(output, filename));
            }
            catch (Exception ex)
            {
                output.Dispose();
                Logger.Error(ex, $"Could not import UTZ package '{task.Path}'.");
                notifications?.Post(new SimpleNotification
                {
                    Text = $"Could not import {Path.GetFileName(task.Path)}: {ex.Message}",
                });
            }
        }

        if (convertedTasks.Count > 0)
            await beatmapManager.Import(convertedTasks.ToArray(), parameters).ConfigureAwait(false);
    }
}
