// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENSE file in the repository root for full licence text.

using System;
using System.Linq;
using System.Reflection;
using osu.Framework;
using osu.Framework.Bindables;
using osu.Framework.Platform;
using osu.Framework.Testing;
using osu.Game.Tests;

namespace osu.Game.Rulesets.Uta.Tests;

/// <summary>
/// Interactive host for Uta <c>TestScene</c>s. Same entry as osu's
/// <c>osu.Game.Tests.VisualTestRunner</c>: <c>dotnet run</c> this project.
/// Pass <c>--run-original-vocals</c> to load and run all leftover-VOX steps.
/// </summary>
public static class VisualTestRunner
{
    [STAThread]
    public static int Main(string[] args)
    {
        using DesktopGameHost host = Host.GetSuitableDesktopHost(@"osu-uta-visual-tests");
        Type? auto = args.Contains("--run-leftover")
            ? typeof(TestSceneUtaPresentBeatmap)
            : args.Contains("--run-original-vocals")
                ? typeof(TestSceneUtaOriginalVocals)
                : null;
        host.Run(new UtaTestBrowser(auto));
        return 0;
    }
}

public partial class UtaTestBrowser : OsuTestBrowser
{
    private readonly Type? autoRun;

    public UtaTestBrowser(Type? autoRun)
    {
        this.autoRun = autoRun;
    }

    protected override void LoadComplete()
    {
        base.LoadComplete();
        if (autoRun == null)
            return;

        Scheduler.AddDelayed(() =>
        {
            TestBrowser? browser = this.ChildrenOfType<TestBrowser>().FirstOrDefault();
            if (browser == null)
                return;

            object? runAll = typeof(TestBrowser)
                .GetField("RunAllSteps", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                ?.GetValue(browser);
            if (runAll is Bindable<bool> flag)
                flag.Value = true;

            browser.LoadTest(autoRun);
        }, 250);
    }
}
