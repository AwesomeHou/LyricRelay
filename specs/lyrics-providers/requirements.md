# 多来源歌词 Provider 需求

## 背景

LyricRelay 已有 LRCLIB 通用歌词链路，但对 QQ 音乐歌曲的覆盖不足，导致手机端已正确发送播放状态、Windows 也收到状态，却可能没有可显示歌词。

本变更先对齐 TaskbarLyrics README 中的四个在线来源：QQ 音乐、网易云音乐、酷狗音乐、LRCLIB。策略是优先使用与当前播放器匹配的歌词来源，失败后再使用通用来源。

## 用户故事

作为使用 QQ 音乐听歌的用户，我希望 Windows 优先从 QQ 音乐歌词来源获取当前歌曲的时间轴歌词；当 QQ 来源不可用时，程序仍能尝试 LRCLIB，而不是直接放弃。

作为使用其他 Android 播放器的用户，我希望网易云音乐、酷狗音乐和 LRCLIB 等来源都能按顺序尝试，不因单一来源失败而失去歌词。

## 验收规则

### R1. Provider 路由

- 当播放状态的 `packageName` 为 `com.tencent.qqmusic` 时，系统应先调用 QQ Music Provider。
- 当 QQ Music Provider 返回未找到、无时间轴、网络错误、超时或格式错误时，系统应继续调用 LRCLIB。
- 当播放器不是 QQ 音乐时，系统不应调用 QQ Music Provider，应优先尝试当前播放器对应 Provider，再使用 LRCLIB。
- Provider 的实际来源名称应反映在 Windows 设置窗口的歌词来源状态中。

### R2. 在线 Provider

- Provider 应只使用歌曲标题、歌手、专辑和时长等已有 Metadata，不读取或传输手机音频。
- QQ Music Provider 应通过 QQ 音乐歌曲搜索结果定位歌曲，再请求对应的时间轴歌词。
- NetEase Provider 应通过网易云歌曲搜索结果定位歌曲，再请求对应的时间轴歌词。
- KuGou Provider 应通过酷狗歌曲搜索结果定位歌曲，再请求对应的时间轴歌词。
- 三个 Provider 都应解析各自返回的时间轴文本或编码包装，并转换为现有 `LyricsTimeline`；MVP 当前在线接口路径统一落到可解析的 LRC 时间轴。
- 歌曲候选与当前 Metadata 明显不匹配时，应返回失败并允许后续 Provider 接管。
- MVP 不要求各平台账号登录、VIP 能力、翻译歌词或歌词上传。

### R3. LRCLIB 兼容性

- 现有 LRCLIB 查询和 LRC 解析行为保持不变。
- LRCLIB 仍可作为所有播放器的通用兜底来源。
- 单个 Provider 的失败不得终止设备连接、播放状态同步或任务栏渲染循环。

### R4. 轻量与安全

- 不新增服务器、账号系统或云端中继。
- 每个 Provider 请求必须有超时，并遵守取消旧歌曲请求的现有机制。
- 日志不得记录完整歌词、配对密钥、完整请求响应或不必要的用户数据。

### R5. 测试与验收

- 单元测试覆盖 Provider 路由、各在线 Provider 返回解析、候选匹配和失败后继续兜底。
- 现有 Windows Core 测试、Windows Client 编译和 Android 构建必须继续通过。
- 使用 QQ 音乐、网易云音乐或酷狗音乐播放一首对应来源可查到歌词的歌曲时，Windows 应显示正确来源，并在任务栏显示时间轴歌词。
- 使用当前 QQ Music Provider 查不到的歌曲时，Windows 应显示可解释的无歌词状态，而不是让连接断开。

## 不在本次范围

- Windows 本地歌词目录、`.lrc/.qrc/.krc` 文件和音频内嵌歌词，后续阶段再加入。
- 歌词缓存、翻译、歌词上传、账号登录和 VIP 资源。
