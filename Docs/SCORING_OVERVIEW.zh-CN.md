# uta! 评分 v2 与演唱历史归档概览

## 本包的定位

当前版本已将评分公式、osu!lazer 原生评分接入、Bad 判定、实时麦克风时序、
颤音与 RMS 分析，以及文件夹式历史回放/录音归档整合为一套运行时实现。

实时麦克风帧会经过有界队列、时间轴映射和 streaming session，提交到 native
judgement、HUD、results 与 performance archive。改变评分选项或跨已提交音符
seek 时会切换 timeline epoch，并把本次游玩标记为不可比较，避免混合不同合同。

## 最终评分语义

### 成绩等级

```text
Perfect → lazer Perfect
Great   → lazer Great
Good    → lazer Good
Bad     → lazer Meh（结果界面显示为 Bad）
Miss    → lazer Miss
Ignored → lazer IgnoreHit
```

### 独立诊断

```text
High
Low
Unstable
Inaccurate
LowCoverage
```

因此一个音符可以是：

```text
Bad + High       (+82 cents)
Bad + Unstable   (稳定度 31%)
Miss + LowCoverage
```

Bad 延续 lazer 原生 combo；另设 Accurate Streak，只允许 Perfect、Great、Good
延续。主分不使用 combo multiplier，以免制谱切音方式改变总分。

## 确定性计分

- 麦克风分析输出先通过有界 `UtaCaptureFrameQueue`；
- gameplay 线程用 `UtaGameplayTimelineMapper` 把真实捕获时间映射成歌曲时间；
- 固定重采样到全局 20 ms 网格；
- 时间、MIDI cents、clarity 和累计权重都在评分边界量化；
- seek/loop 通过 timeline epoch 隔离；
- 队列溢出不会静默丢分，而会令本次 performance 标为不可比较。

## 综合分

核心维度：

```text
A = 音程准确度
C = 发声覆盖率
S = 稳定度
T = 长音/颤音技术质量
```

音程 gate 防止“稳定地唱错音”依靠稳定或颤音补回大量分数：

```text
gate(A) = clamp((A - 0.550) / 0.300, 0, 1)
```

公开画像：

```text
Faithful  = 0.940 A + 0.060 Sg
Stable    = 0.900 A + 0.100 Sg
Technique = 0.880 A + 0.060 Sg + 0.060 Tg
```

取整场累加后最高画像，标准化到 1,000,000。没有可分析技术段的短音在
Technique 画像中回退到 Faithful，不会把整首歌的技术画像分母白白拉低。

## lazer 本地成绩与 uta! 历史文件

采用双层存储：

```text
lazer ScoreInfo
  总分、Accuracy、Rank、Max Combo、判定统计、MOD、日期、谱面关联

uta! performance archive
  Pitch replay、逐音符/逐句分析、设置快照、可选录音、波形缓存、checksum
```

目录：

```text
<root>/
├── index-v1.json
└── performances/<performance-id>/
    ├── performance.json
    ├── pitch-replay.jsonl.br
    ├── take.wav          # 可选
    ├── waveform.bin      # 可选
    └── complete
```

`performance.json` 是事实来源，索引可以重建。写入采用 `.partial-*` 临时目录、
SHA-256 和完成标记。归档失败不阻止 lazer 原生成绩保存。

录音约定为应用 input gain 后、进入 monitor routing 前；默认关闭，启用时必须
持续显示录音状态。

## 详细文档

- `SCORING.md`：评分合同和公式；
- `PERFORMANCE_ARCHIVE.md`：历史回放与录音目录协议；
- `TESTING.md`：自动测试、真实游戏验证和交付门槛；
- `CHANGELOG.md`：各版本已完成的运行时接入记录。
