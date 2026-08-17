// Copyright (c) bintis. Licensed under the GPL Licence.
// See the LICENSE file in the repository root for full licence text.

using System;
using System.Collections.Generic;
using osu.Framework.Configuration;

namespace osu.Game.Rulesets.Uta.Localisation;

/// <summary>
/// The three UI languages Uta's own hand-drawn HUD text supports.
/// </summary>
internal enum UtaUiLanguage
{
    English,
    ChineseSimplified,
    Japanese,
}

/// <summary>
/// Maps lazer's global <see cref="FrameworkSetting.Locale"/> (already offering Chinese and
/// Japanese among many others) onto the three languages Uta's HUD text is written in. Uta's own
/// panels are drawn directly (not through <c>TranslatableString</c>/<c>ILocalisationStore</c>),
/// so this reads the same underlying bindable lazer's own UI reacts to rather than adding a
/// separate, ruleset-only language switch.
/// </summary>
internal static class UtaLanguageResolver
{
    public static UtaUiLanguage FromLocale(string? locale)
    {
        if (string.IsNullOrEmpty(locale))
            return UtaUiLanguage.English;

        if (locale.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
            return UtaUiLanguage.ChineseSimplified;
        if (locale.StartsWith("ja", StringComparison.OrdinalIgnoreCase))
            return UtaUiLanguage.Japanese;

        return UtaUiLanguage.English;
    }
}

/// <summary>
/// Hand-maintained string table for Uta's own HUD panels (score HUD, practice HUD). Not routed
/// through lazer's crowd-sourced translation pipeline - that only covers strings that ship inside
/// osu.Game itself, not third-party ruleset text - so these three languages are maintained here
/// directly instead.
/// </summary>
internal static class UtaStrings
{
    private static readonly Dictionary<string, (string En, string Zh, string Ja)> table = new()
    {
        ["hud.composite"] = ("Composite", "综合", "総合"),
        ["hud.pitch"] = ("Pitch", "音程", "音程"),
        ["hud.coverage"] = ("Coverage", "覆盖", "カバー率"),
        ["hud.combo"] = ("Combo", "连击", "コンボ"),
        ["hud.accurate"] = ("Accurate", "精准", "正確"),
        ["hud.current_note"] = ("Current note", "当前音符", "現在の音符"),

        ["fault.high"] = ("High", "偏高", "高い"),
        ["fault.low"] = ("Low", "偏低", "低い"),
        ["fault.unstable"] = ("Unstable", "不稳定", "不安定"),
        ["fault.low_coverage"] = ("Low coverage", "覆盖不足", "カバー不足"),
        ["fault.inaccurate"] = ("Inaccurate", "不准确", "不正確"),

        ["practice.title"] = ("Practice", "练习", "練習"),
        ["practice.speed"] = ("Speed", "速度", "速度"),
        ["practice.speed_tooltip"] = (
            "Pitch-preserving practice speed - adjustable live, mid-song.",
            "变调保持的练习速度，可在歌曲进行中实时调整。",
            "ピッチを保ったまま再生速度を変更 - 曲の途中でもリアルタイムに調整できます。"),
        ["practice.current_speed"] = ("Current speed: {0:0}%", "当前速度：{0:0}%", "現在の速度：{0:0}%"),
        ["practice.reset_speed"] = ("Reset speed", "重置速度", "速度をリセット"),
        ["practice.set_loop_a"] = ("Set loop A", "设置循环点 A", "ループ地点Aを設定"),
        ["practice.set_loop_b"] = ("Set loop B", "设置循环点 B", "ループ地点Bを設定"),
        ["practice.clear_loop"] = ("Clear loop", "清除循环", "ループを解除"),
        ["practice.loop_current_phrase"] = ("Loop current phrase", "循环当前乐句", "現在のフレーズをループ"),
        ["practice.previous_phrase"] = ("Previous phrase", "上一乐句", "前のフレーズ"),
        ["practice.retry_phrase"] = ("Retry phrase", "重试乐句", "フレーズをリトライ"),
        ["practice.next_phrase"] = ("Next phrase", "下一乐句", "次のフレーズ"),
        ["practice.loop_status_looping"] = ("Looping current phrase ({0} detected)", "正在循环当前乐句（检测到 {0} 句）", "現在のフレーズをループ中（{0} 個検出）"),
        ["practice.loop_status_points"] = ("Loop A {0}  B {1}", "循环点 A {0}  B {1}", "ループ A {0}  B {1}"),
    };

    public static string Get(string key, UtaUiLanguage language)
    {
        if (!table.TryGetValue(key, out var entry))
            return key;

        return language switch
        {
            UtaUiLanguage.ChineseSimplified => entry.Zh,
            UtaUiLanguage.Japanese => entry.Ja,
            _ => entry.En,
        };
    }
}
