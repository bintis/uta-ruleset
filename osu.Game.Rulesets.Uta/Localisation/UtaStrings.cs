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
        ["hud.stability"] = ("Stability", "稳定", "安定度"),
        ["hud.phrase"] = ("Phrase", "段落", "フレーズ"),
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

        ["import.title"] = ("Recent .utz import diagnostics", "最近的 .utz 导入诊断", "最近の .utz インポート診断"),
        ["import.refresh"] = ("Refresh import diagnostics", "刷新导入诊断", "インポート診断を更新"),
        ["import.clear"] = ("Clear import diagnostics", "清除导入诊断", "インポート診断を消去"),
        ["import.none"] = ("No failed .utz imports have been recorded in this process.", "本次运行尚未记录失败的 .utz 导入。", "この実行では失敗した .utz インポートは記録されていません。"),

        ["remote.title"] = ("uta! mobile remote", "uta! 手机遥控", "uta! モバイルリモコン"),
        ["remote.start"] = ("Start server", "启动服务", "サーバーを開始"),
        ["remote.stop"] = ("Stop server", "停止服务", "サーバーを停止"),
        ["remote.disconnect_all"] = ("Disconnect all clients", "断开所有客户端", "すべてのクライアントを切断"),
        ["common.close"] = ("Close", "关闭", "閉じる"),
        ["remote.pairing_expires"] = ("Controller pairing expires {0}", "控制器配对将在 {0} 失效", "コントローラーのペアリングは {0} に期限切れになります"),
        ["remote.stops_in"] = ("stops in {0}s", "将在 {0} 秒后停止", "{0} 秒後に停止"),
        ["remote.clients"] = ("Authenticated controllers {0} · gameplay {1}", "已认证控制器 {0} · 游戏 {1}", "認証済みコントローラー {0} · ゲームプレイ {1}"),
        ["remote.gameplay_none"] = ("none", "无", "なし"),
        ["remote.gameplay_active"] = ("active", "进行中", "進行中"),

        ["settings.reset_display"] = ("Reset display settings", "重置显示设置", "表示設定をリセット"),
        ["settings.reset_playback"] = ("Reset audio and latency settings", "重置音频与延迟设置", "音声とレイテンシ設定をリセット"),
        ["settings.reset_microphone"] = ("Reset microphone settings", "重置麦克风设置", "マイク設定をリセット"),

        ["queue.title"] = ("uta! global queue", "uta! 全局队列", "uta! グローバルキュー"),
        ["queue.play_next"] = ("Play next", "播放下一首", "次を再生"),
        ["queue.end_song"] = ("End song", "结束歌曲", "曲を終了"),
        ["queue.add_songs"] = ("Add songs", "添加歌曲", "曲を追加"),
        ["queue.queue"] = ("Queue", "队列", "キュー"),
        ["queue.clear"] = ("Clear queue", "清空队列", "キューを消去"),
        ["queue.search"] = ("Search Uta songs to add...", "搜索要添加的 Uta 歌曲…", "追加する Uta 曲を検索…"),
        ["queue.add"] = ("Add", "添加", "追加"),
        ["queue.play"] = ("Play", "播放", "再生"),
        ["queue.top"] = ("Top", "置顶", "先頭"),
        ["queue.up"] = ("Up", "上移", "上へ"),
        ["queue.down"] = ("Down", "下移", "下へ"),
        ["queue.bottom"] = ("Bottom", "置底", "末尾"),
        ["queue.remove"] = ("Remove", "移除", "削除"),
        ["queue.empty"] = ("The Uta queue is empty.", "Uta 队列为空。", "Uta キューは空です。"),
        ["queue.status"] = ("{0} song(s) · revision {1} · {2}", "{0} 首歌曲 · 修订 {1} · {2}", "{0} 曲 · リビジョン {1} · {2}"),

        ["archive.title"] = ("Uta performance archive", "Uta 演唱档案", "Uta パフォーマンスアーカイブ"),
        ["archive.play_replay"] = ("Play pitch replay", "播放音高回放", "ピッチリプレイを再生"),
        ["archive.open_recording"] = ("Open recording", "打开录音", "録音を開く"),
        ["archive.open"] = ("Open archive", "打开档案", "アーカイブを開く"),
        ["archive.searching"] = ("Searching performance archive...", "正在搜索演唱档案…", "パフォーマンスアーカイブを検索中…"),
        ["archive.replay_not_loaded"] = ("Pitch replay not loaded.", "未加载音高回放。", "ピッチリプレイは未読み込みです。"),
        ["archive.replay_again"] = ("Replay again", "再次回放", "もう一度再生"),
        ["archive.resume_replay"] = ("Resume pitch replay", "继续音高回放", "ピッチリプレイを再開"),
        ["archive.pause_replay"] = ("Pause pitch replay", "暂停音高回放", "ピッチリプレイを一時停止"),
        ["archive.complete"] = ("complete", "完成", "完了"),
        ["archive.native_score_available"] = ("The native lazer score remains available.", "lazer 原生成绩仍然可用。", "lazer のネイティブスコアは引き続き利用できます。"),
        ["archive.no_replay"] = ("This archive has no pitch replay.", "该档案没有音高回放。", "このアーカイブにはピッチリプレイがありません。"),
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
