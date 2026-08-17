# uta! 0.7 评分与录音运行时接入说明

## 基准与范围

本实现以 uta-ruleset `main` 提交
`72cfb9f0fbd4142f44fa7253a093978173c3a6c0` 为基准，并按项目当前目标
osu!lazer `2026.804.2-lazer` 的公开 ruleset API 接线。

这不是再次放入一组“以后可用”的 scoring primitive，而是把已经存在的 v2
评分内核接入实际 gameplay：开启 `评分模式`（`SC`）后，麦克风帧经过正式队列和
时间映射进入 scorer，每个可评分 `UtaNote` 在结束后产生 lazer 原生 judgement，
`UtaScoreProcessor` 负责 native score。未开启时使用 ignored judgement，不计算分数。

当前落地范围：

- realtime microphone → deterministic scorer；
- `UtaJudgementResult`；
- `UtaScoreProcessor`；
- native `ScoreInfo` 本地成绩历史；
- live score HUD；
- v2 vibrato 分析；
- report-only RMS 表现力分析；
- performance JSON/Pitch replay 文件夹归档；
- 结果页历史查询和 Pitch replay 预览；
- `评分模式`（`SC`）MOD 与 note-driven health；
- practice/seek/rate/latency/queue-overflow 的可比性标记。

0.6 已接入麦克风 PCM/WAV writer；0.7 将录音改为显式 `Recording`（`REC`）MOD。
只有选择该 MOD 的游玩才采集并保存 `take.wav`，采集点继续遵守“输入增益后、
monitor routing 前”的合同。

## 1. 运行时数据流

```text
BASS microphone callback
    ↓
UtaPitchDetector worker
    ↓ UtaPitchFrame（monotonic arrival timestamp）
UtaCaptureFrameQueue（有界、不可静默覆盖）
    ↓ gameplay update thread
UtaGameplayTimelineMapper
    ↓ UtaScoringFrame（song time / cents / clarity / epoch）
UtaStreamingScoringSession
    ↓ completed UtaNoteScore
DrawableUtaHitObject
    ↓ UtaJudgementResult
DrawableRuleset.NewResult
    ↓
HealthProcessor → UtaScoreProcessor → GameplayState
```

显示曲线仍使用原有 latest-only mailbox 和平滑 MIDI。正式 scorer 不读取显示平滑
结果，也不按 callback 数量加分。

## 2. 时间与延迟

每个 Pitch window 的正式时间先在真实 monotonic 时间轴上计算：

```text
capture centre = arrival timestamp
               - analysis window / 2
               - signed microphone latency
```

再由分段 timeline mapper 按当时的 playback rate 映射到 song time。麦克风延迟
允许 `-500 ms` 到 `+1000 ms`；负值不能在 queue/mapper 层被拒绝。

Pause、resume、rate 变化只增加同一 epoch 的新锚点。以下操作开始新 epoch，并将
该 performance 标记为 practice/non-comparable：

- backward seek；
- A-B loop repeat；
- retry/previous/next phrase；
- forward seek 跨过可评分 note；
- 评分映射设置在演唱中发生变化。

lazer 的 intro/outro skip 和无目标长间奏 skip 若只向前跨越无评分区间，则保持同一
scoring epoch，不会仅因使用正常 skip 功能而失去可比性。

## 3. Native judgement

只有开启 `评分模式`（`SC`）后，Pitch-scored note 使用：

```text
UtaJudgement: Perfect … Miss
```

未开启 `评分模式` 时的所有 note，以及开启后仍属于 rap、spoken、freestyle、
无 MIDI 或低 target-confidence 的 note，使用：

```text
UtaIgnoredJudgement: IgnoreHit … IgnoreMiss
```

整音符评分结束后，drawable 将同一个 `UtaNoteScore` 的固定点字段复制到
`UtaJudgementResult`。没有第二套 HUD 分数或 draw-thread 估算。

映射：

| uta! grade | lazer HitResult | native combo | Accurate streak |
|---|---|---:|---:|
| Perfect | Perfect | +1 | +1 |
| Great | Great | +1 | +1 |
| Good | Good | +1 | +1 |
| Bad | Meh | +1 | reset |
| Miss | Miss | reset | reset |
| Ignored | IgnoreHit | unchanged | unchanged |

`Bad` 是成绩等级；`High`、`Low`、`Unstable`、`Inaccurate`、`LowCoverage`
是独立 fault flags。结果页对 native `Meh` 显示名称覆盖为 `Bad`。

## 4. UtaScoreProcessor

`UtaRuleset.CreateScoreProcessor()` 现在返回 `UtaScoreProcessor`。只有 beatmap
由 `评分模式` 标记为可评分时，它才在 native apply/revert 中逐项加减并输出
`0..1,000,000` 分；普通 karaoke play 的总分、准确率和 Uta rating 均保持为 0。

评分开启后逐项累计：

```text
MaximumUnits
PitchEarnedUnits
VoicedUnits
FaithfulEarnedUnits
StableEarnedUnits
TechniqueEarnedUnits
Accurate-streak before/after values
```

最终 native 分数范围仍为 `0..1,000,000`。`TotalScoreWithoutMods`、MOD multiplier、
rank、statistics、maximum statistics 和 local `ScoreInfo` 导入继续由 lazer 管理。

因此 non-legacy custom ruleset 即使不经过 legacy `.osr` encoder，也能进入本地成绩
历史。缺失 `.osr` 只意味着详细 Pitch/录音回放需要 uta! 自己的 performance archive。

## 5. Health 与评分模式 MOD

默认规则集返回 `UtaPassiveHealthProcessor`：

- health 保持满值；
- 不发生默认失败；
- 普通 karaoke play 不受连续 drain 影响。

原 `Fail` 命名已移除，替换为：

```text
Name: 评分模式
Acronym: SC
Class: UtaModScoringMode
Processor: UtaScoringModeHealthProcessor
```

评分模式只在完成 note judgement 时调整 health：

```text
noteShare  = note.MaximumUnits / song.MaximumUnits
healthDelta = noteShare × scale × (noteQuality - neutralQuality)
```

Intro、休止、换气和无 target 区域不持续掉血。HealthProcessor 保存每次 judgement
前 health，因此 lazer result revert 能恢复精确状态。

## 6. Live HUD

仅开启 `评分模式` 时才创建 `UtaScoringHud`。它绑定 `UtaScoreProcessor` 与
runtime controller：

```text
Total score
Composite rating
Pitch accuracy
Coverage
Native combo
Accurate streak
Selected profile
Last note grade/fault/bias
Archive state
```

Committed score、combo 和 health 只在 native judgement 后变化；当前 Pitch 曲线继续
实时绘制，但不会提前增加分数。

## 7. Performance archive

Native `ScoreInfo` 是轻量、稳定的成绩事实；开启 `评分模式` 或 `Recording` 时，
uta! archive 保存本次游玩的详细数据。仅 `Recording` 时评分字段为空分，仍保存 Pitch
replay 与 `take.wav`：

```text
<UtaPerformanceRoot>/
├── index-v1.json
└── performances/
    └── <performance-id>/
        ├── performance.json
        ├── pitch-replay.jsonl.br
        ├── take.wav          # 仅 Recording (REC) MOD
        ├── waveform.bin      # future/optional
        └── complete
```

归档根目录按以下顺序解析：

1. 环境变量 `UTA_PERFORMANCE_ROOT`；
2. osu! storage 下 `exports/uta-performance-root.txt` 中记录的路径；
3. 默认 `exports/uta-performances`。

例如 Linux：

```bash
export UTA_PERFORMANCE_ROOT="$HOME/Music/uta-performances"
```

或者在 `exports/uta-performance-root.txt` 中写入一个绝对路径。第一次成功保存后，
runtime 会尽力把实际路径原子写回该 pointer file。

归档失败不会阻止 native score 保存。HUD/结果页只提示详细 archive 不可用。

## 8. 可比性

`performance.json` 持久化可比性与原因：

```text
practice_session
automation
timeline_discontinuity
scoring_queue_overflow
late_scoring_frame
settings_changed_during_play
capture_unavailable
incomplete_performance
```

正常 forward gap skip 不自动失去可比性。A-B loop、phrase retry、跨 note seek 或正式
capture queue overflow 会明确标记 non-comparable，不会伪装成可排名 performance。

## 9. 结果页与历史播放

`UtaRuleset.CreateStatisticsForScore()` 增加 Uta performance panel：

```text
ScoreInfo.ID
→ UtaPerformanceLibrary.FindByLazerScoreId()
→ performance.json
→ pitch-replay.jsonl.br
```

归档存在时显示：

- overall metrics；
- Perfect/Great/Good/Bad/Miss；
- High/Low/Unstable；
- RMS dynamic range；
- Pitch replay 时间序列预览；
- recording/archive 文件入口。

归档不存在或被移动时，native score/rank/statistics 仍正常显示。

当前历史播放是结果页内的 Pitch frame 预览。完整的“跟随原歌曲时钟重放 Pitch 曲线、
评分判定与可选 take.wav 混音”应在 recording/playback PR 中建立独立
`UtaReplayPlaybackController`，不能重新打开 live microphone。

## 10. Vibrato 与 RMS

Vibrato 使用已有固定 20 ms grid、去趋势 autocorrelation、周期/幅度/漂移门限，作为
Technique profile 的有限、pitch-gated 质量项；不会按次数无限加分。

RMS runtime 输入进入 `UtaExpressionAnalyzer`，只生成报告：P10、median、P90、动态
范围和可能的 AGC/压缩提示。当前 detector frame 尚未提供 raw peak，因此 clipping
比例保持保守值；PCM recording 从同一 post-gain/pre-monitor 流采集。

## 11. 导入后的验证

```bash
dotnet restore osu.Game.Rulesets.Uta.Tests/osu.Game.Rulesets.Uta.Tests.csproj

dotnet format osu.Game.Rulesets.Uta/osu.Game.Rulesets.Uta.csproj \
  --no-restore --verify-no-changes

dotnet build osu.Game.Rulesets.Uta/osu.Game.Rulesets.Uta.csproj \
  -c Release --no-restore

dotnet test osu.Game.Rulesets.Uta.Tests/osu.Game.Rulesets.Uta.Tests.csproj \
  -c Release --no-restore
```

重点手工验证：

- Perfect/Great/Good/Bad/Miss native statistics；
- Bad 保持 native combo、重置 Accurate streak；
- 评分模式 Miss/Bad health 行为；
- negative latency；
- 0.5x/1.0x/1.5x；
- intro/gap skip；
- A-B loop 与 phrase retry；
- archive 写入失败时 native score 仍保存；
- 历史 score ID 查询。
