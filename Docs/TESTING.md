# uta-ruleset 测试方法

本文档是本仓库统一的测试与交付检查入口。通用构建、自动化测试、真实 osu! 冒烟测试、日志判定和历史专项测试均以此处为准。

## 1. 测试环境

- .NET 8 / C# 12
- 本机 osu!lazer 安装作为 API 与运行时的权威来源
- ruleset 部署目录：
  `/mnt/Files/App/Songs/osu-lazer/rulesets`
- 运行日志目录：
  `/mnt/Files/App/Songs/osu-lazer/logs`
- F12 截图目录：
  `/mnt/Files/App/Songs/osu-lazer/screenshots`

### 所有实机/UI 测试的显示器与坐标前置条件

只要测试会启动图形程序、读取截图、点击 UI、扫码或注入键鼠，就必须先确认应用实际位于目标显示器，且窗口/全屏分辨率、DPI 缩放与该显示器一致。多显示器、HiDPI/4K 和 XWayland 环境可能使逻辑坐标与物理像素坐标不同；显示器不对时，截图比例、自动化坐标和二维码物理尺寸均不可信，不能据此判定功能失败。

在 COSMIC 中可通过窗口概览将应用拖到目标显示器，或聚焦窗口后使用窗口移动快捷键。移动后应重启应用，并立即截图确认窗口完整可见、截图来自预期显示器且比例正确；否则先修正显示器/分辨率，禁止猜测缩放坐标后继续点击。

测试前应退出 osu!，避免进程继续持有旧 DLL。

## 2. 自动化回归测试

### 2.1 使用本机 Nix osu! 引用

这是 Linux/Nix 开发机上的主要验证路径：

```sh
OSU_BIN="$(readlink -f "$(command -v 'osu!')")"
OSU_ROOT="$(dirname "$(dirname "$OSU_BIN")")"
OSU_NIX_DIR="$OSU_ROOT/lib/osu-lazer"

dotnet test osu.Game.Rulesets.Uta.Tests/osu.Game.Rulesets.Uta.Tests.csproj \
  -c Release -p:OsuNixDir="$OSU_NIX_DIR" --no-restore
```

要求：0 failed，且不能新增非预期 skipped 测试。

### 2.2 NuGet fallback

用于确认项目在没有 `OsuNixDir` 时仍可使用 csproj 中固定的 `ppy.osu.Game` 包：

```sh
dotnet test osu.Game.Rulesets.Uta.Tests/osu.Game.Rulesets.Uta.Tests.csproj \
  -c Release --no-restore
```

### 2.3 格式与静态检查

```sh
dotnet format osu.Game.Rulesets.Uta/osu.Game.Rulesets.Uta.csproj \
  --no-restore --verify-no-changes
python3 scripts/audit_overlay.py
git diff --check
```

`audit_overlay.py` 检查 overlay 生命周期、依赖注入和已知热路径约束；它不能替代编译或真实游戏测试。

### 2.4 运行时硬化回归目标

`UtaRuntimeHardeningTests` 是 README 运行时测试项的确定性覆盖：

- 全组合验证 Transpose、OCT、0.75/1.0/1.5 倍率（DC/normal/NC）、±500 ms 麦克风延迟和 loop/retry epoch 的评分结果；
- 连续 20 次录音 retry，验证每次 WAV 的完整帧数和资源释放；
- 验证设备格式变化与存储写失败会明确 fault，绝不伪称录音完整；
- 用十分钟等效的 600-note/30,000-frame 流式评分工作负载，验证实时帧窗口有界且最终不残留缓冲；
- 连续启动/停止 10 次私网远程服务，验证监听器释放。

该组与 `UtaHotPathRegressionTests`、`UtaRecordingAndFollowUpTests`、`UtaScoringV2Tests` 和远程配对回归共同构成音频、麦克风、评分、录音和远程热路径的自动化测试。真实设备热插拔和长时间真机测试仍按第 5、6 节执行，不能用模拟测试代替。

### 2.5 支持的平台构建矩阵

当前发布支持并在此工作站验证的桌面目标是 **Linux x64**：

| 依赖解析路径 | 验证命令 | 结果 |
|---|---|---|
| 本机 Nix osu! (`2026.726.0`) | §2.1 | 165 passed |
| NuGet `ppy.osu.Game` fallback | §2.2 | 165 passed |

Windows/macOS 尚未作为发布支持目标；不得把未执行的跨平台构建写成已验证。每次更新 osu! Nix 包后须重新执行两条命令并更新此表。

## 3. 构建与部署

始终用本机 osu! 引用完成最终构建，然后部署同一个产物：

```sh
OSU_BIN="$(readlink -f "$(command -v 'osu!')")"
OSU_ROOT="$(dirname "$(dirname "$OSU_BIN")")"
OSU_NIX_DIR="$OSU_ROOT/lib/osu-lazer"

dotnet build osu.Game.Rulesets.Uta/osu.Game.Rulesets.Uta.csproj \
  -c Release -p:OsuNixDir="$OSU_NIX_DIR" --no-restore

install -m 0644 \
  osu.Game.Rulesets.Uta/bin/Release/net8.0/osu.Game.Rulesets.Uta.dll \
  /mnt/Files/App/Songs/osu-lazer/rulesets/osu.Game.Rulesets.Uta.dll

sha256sum \
  osu.Game.Rulesets.Uta/bin/Release/net8.0/osu.Game.Rulesets.Uta.dll \
  /mnt/Files/App/Songs/osu-lazer/rulesets/osu.Game.Rulesets.Uta.dll
```

两个 SHA-256 必须完全一致。最终 Nix 构建之后不要再用 NuGet fallback 构建覆盖部署产物。

## 4. 开启运行诊断

在 uta! 设置中开启 `Debug diagnostics`。诊断日志每约 5 秒输出：

- `Uta debug ruleset mods`：DrawableRuleset 实际收到的 MOD；
- `Uta game-wide selected mods changed`：选曲界面的权威 MOD 快照；
- `Uta debug audio`：gameplay clock、frequency、tempo、BGM/VOX 位置与漂移；
- `Uta debug settings`：Transpose、延迟、音量、输入增益和设备路由；
- `Uta debug microphone`：采集频率、分析队列、丢帧和输入峰值；
- `Uta vocals skipped/route ready/volume applied`：人声轨是否实际创建和播放。

读取最新日志：

```sh
LOGDIR=/mnt/Files/App/Songs/osu-lazer/logs
LATEST="$(find "$LOGDIR" -maxdepth 1 -name '*.runtime.log' -type f \
  -printf '%T@ %p\n' | sort -nr | head -1 | cut -d' ' -f2-)"
echo "$LATEST"

rg -ni 'Uta (debug ruleset mods|game-wide selected mods|debug audio|debug settings|debug microphone|vocals|repaired)|error|exception' "$LATEST"
```

不要只凭听感判定音频问题；同时核对实际 MOD、有效音量、轨道位置、倍率和设备。

## 5. 真实 osu! 自动/半自动冒烟测试

### 5.1 启动与截图

遵循上方的通用显示器与坐标前置条件。启动前额外检查 `framework.ini` 的 `LastDisplayDevice`、`WindowedSize`、`WindowedPositionX/Y` 和 `WindowMode`。

部署后在已确认的目标显示器启动 osu!：

```sh
nohup osu! >/tmp/uta-osu-smoke.out 2>&1 &
```

F12 使用 osu! 自己的截图功能。截图会保存到：

```text
/mnt/Files/App/Songs/osu-lazer/screenshots
```

在 XWayland 环境可用 `xdotool` 驱动键盘。为避免残留 Ctrl/Alt/Shift 影响输入，使用：

```sh
xdotool key --clearmodifiers KEY
```

#### 5.1.1 COSMIC、多显示器与 4K 缩放记录

先用以下命令记录**本次**坐标系，而不是套用上次的坐标：

```sh
xdotool getdisplaygeometry
xdotool search --name 'osu!' getwindowgeometry %@
mkdir -p /tmp/uta-cosmic-screenshots
cosmic-screenshot --interactive=false --modal=false --notify=false \
  --save-dir /tmp/uta-cosmic-screenshots
```

`osu!` 的 F12 截图只包含游戏窗口，不能用来判断它在哪块物理显示器；使用 `cosmic-screenshot` 的全桌面 PNG 判断。当前工作站曾出现：DRM 输出同时连接 `1920x1080` 与 `3840x2160`，4K 输出以 2× 缩放参与 COSMIC 桌面；XWayland 报告 `1920x1080`，而全桌面截图为 `3840x1080`。此时 `xdotool` 和全桌面截图不能直接互换坐标，窗口点击自动化一律视为无效，优先在 COSMIC 概览中拖动应用到目标显示器后重启 lazer。

若暂时无法移动窗口，必须在测试记录中保存上述三项输出、F12 截图和全桌面截图，并只使用已确认的键盘流程；不得凭缩放后的像素坐标点击。

#### 5.1.2 菜单与选曲流程

1. 未登录可使用 Guest；登录浮层遮挡时先按 `Escape` 关闭，不要把账号页当作游戏主菜单。
2. 聚焦 lazer 后连续按两次 `P`，依次展开主菜单与 Play 子菜单；确认出现 `Solo` 后再继续。不要假定单独的 `S` 在所有焦点状态下都能进入 Solo。
3. 在分辨率和坐标已确认时点击 `Solo`；进入选曲界面后直接输入测试谱面标题，等待筛选完成，再选择谱面并开始。
4. 每个关键状态（关闭登录浮层、Play/Solo、选曲筛选、MOD 面板、游戏开始）各保存一张 F12 截图。状态不明确时停止输入、先截图再判断，禁止盲目连续点击。



### 5.2 必测 MOD 矩阵

对同一首 `uta.song 0.3.x` / `vocal-chart/1` 谱面依次测试：

1. 无 MOD；
2. 仅 NC；
3. 仅 DC；
4. VOX；
5. VOX + Transpose；
6. VOX + NC；
7. VOX + DC；
8. 仅 NC/DC（在前一次开启 VOX 和 Transpose 后再次进入），用于发现持久状态污染。

每组至少越过可跳过的开头并运行 15 秒；音频生命周期修复还应执行进入、退出、重试和加载中取消。

### 5.3 日志判定

#### 仅 NC/DC、未选 VOX

必须同时满足：

```text
originalVocalsEnabled=False
Uta vocals skipped
Uta vocals volume applied: 0%
vocals=n/a
```

若此前使用过 Transpose，而本次没有选择 Transpose，还必须为：

```text
key=0st
```

#### VOX 开启

必须出现 `route ready`，有效音量大于 0，且 `Uta debug audio` 中 VOX 位置持续前进。BGM/VOX 漂移不应持续增长；短时设备缓冲差异通常应保持在几十毫秒内。

#### NC/DC 与 Transpose

NC、DC 与显式 Transpose（K±）在 MOD 选择器中双向互斥，不能组合：

- NC 总速率：`rate=1.500`
- DC 总速率：`rate=0.750`
- 单独使用 Transpose `s` 个半音时：
  - `frequency = 2^(s/12)`
  - `tempo = 2^(-s/12)`
  - `frequency × tempo = 1`

BGM、VOX、歌词、Pitch guide 和参考曲线必须共享同一 gameplay 时间轴；不得重复应用倍率。

#### 麦克风与监听

当前 AKG 作为输入时应看到类似：

```text
name='AKG ...' frequency=48000Hz channels=1
dropped=0
```

不要在测试步骤或验收断言中硬编码已经更换的播放设备名称。若捕获设备被错误保存成监听输出，而 BGM/VOX 配置的是另一个可用播放设备，运行时必须把监听的有效输出解析到该播放设备，并记录 `Uta repaired capture device used as monitor output`。不能把输入设备直接当作监听播放端。

`input-peak` 用于判断底噪/过载，`dropped` 用于判断分析队列丢帧。监听音量和输入增益是独立变量；排查噪声时先用 `1.00x` 输入增益，再分别测试 0%、35% 和 100% 监听。

#### IM 手机远程运行时验收

目标：确认运行时启动的远程服务实际监听局域网地址，配对页可加载，WebSocket 可完成 controller 配对；同时保留 fragment 中的一次性 ticket，避免其出现在首次 HTTP 请求中。

自动化回归目标是 `UtaReleaseRegressionTests.TestPairingUrlLoadsRemoteAndPairsOverTheNetworkListener`，它必须：

1. 在私网监听地址和临时 TCP 端口启动 `UtaRemoteServer`；
2. 断言配对 URL 的 `ticket`/`role` 位于 fragment，而不是 query；
3. 通过 HTTP 加载嵌入的手机页面；
4. 模拟手机将 fragment 带入 `/ws` 查询参数，完成二进制 welcome 帧握手。

实机验收：先确认手机连接与电脑 `10.1.1.20` 相同的 Wi-Fi/LAN（不能使用蜂窝网络），然后在 IQ 游戏中扫码。运行日志必须依次出现：

```text
Uta IQ remote pairing prompt shown.
Uta remote HTTP GET / from <phone-ip>.
Uta remote HTTP GET /ws from <phone-ip>.
Uta remote hello role=Controller
```

手机应加载 Control 页并能收到状态；点击 Play/Pause 后日志应出现对应的 `Uta remote command` 和 `Uta remote accepted`。若没有任何 `Uta remote HTTP` 行，先检查手机 Wi-Fi、路由器客户端隔离和 NixOS TCP 27835 防火墙，不要修改配对协议。

#### 加载取消与离场

在 PlayerLoader 中取消、从游戏退出和重试后，日志中不得出现：

```text
Cannot access Track without first calling LoadTrack
```

旧 BGM/VOX 必须停止，SongSelect 预览必须恢复。

## 6. 历史专项方法整理

### 6.1 SongSelect 切歌与音频所有权

历史的 51 步验收可简化为以下循环，连续执行至少三次：

1. 进入带 VOX 的 Uta 谱面并越过开头；
2. 退出到 SongSelect，确认预览恢复；
3. 切换到另一首 Uta 谱面；
4. 再次进入并确认只有新 BGM/VOX 播放；
5. 在加载中取消一次；
6. 检查 Track 异常、残留流和位置漂移。

历史参考日志：`1787161797.runtime.log`、`1787161827.runtime.log`、`1787161857.runtime.log`。

### 6.2 Practice、循环和 seek

- 依次测试 50%、80%、130%、150% Practice Speed；
- 设置 A/B loop 并重复多次；
- 测试 previous/next/retry phrase；
- 测试 current-phrase loop 的 lead-in；
- 同时组合 Transpose、VOX、OCT 和非零 mic/accompaniment/lyrics latency；
- 检查 Pitch trail 在 seek/loop 接缝处清空，不产生斜线拖影。

### 6.3 长时间稳定性

运行至少 15 分钟，检查：

- BGM/VOX 漂移不累积；
- `dropped=0` 或没有持续增长；
- scoring/capture 队列不溢出；
- frame update rate 与 max-gap 无持续恶化；
- 内存、路由流和 Track 数量不随 retry/seek 无限增长。

### 6.4 皮肤回归

```sh
python3 test-skins/build_transparent_fallback_skin.py
python3 scripts/audit_overlay.py
```

加载 `test-skins/Uta-Transparent-Fallback`，确认透明或缺失纹理不能隐藏关键 Pitch、歌词、判定和 HUD 语义 fallback。

## 7. 交付门槛

交付前必须全部满足：

- Nix 引用测试通过；
- NuGet fallback 测试通过；
- format、overlay audit、`git diff --check` 通过；
- Release build 0 errors、0 warnings；
- 真实 osu! 冒烟日志通过本次问题对应的 MOD/音频/麦克风断言；
- 部署 DLL 与构建 DLL 的 SHA-256 相同；
- 无未处理异常、Track 崩溃或旧音频残留；
- 不生成 zip，不 push；是否 commit 按当前任务要求决定。

每次交付应在 `Docs/CHANGELOG.md` 记录测试数量、关键实机日志文件名和最终部署哈希。
