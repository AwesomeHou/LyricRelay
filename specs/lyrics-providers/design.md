# 多来源歌词 Provider 设计

## 目标架构

```text
TrackState
   ↓
TrackQuery(packageName)
   ↓
LyricsCoordinator
   ├─ QQ Music Provider（仅 com.tencent.qqmusic）
   ├─ NetEase Provider（仅 com.netease.cloudmusic）
   ├─ KuGou Provider（仅 com.kugou.android）
   └─ LRCLIB Provider（通用兜底）
   ↓
LyricsTimeline
   ↓
TaskbarRenderer
```

## 模块设计

### TrackQuery

在现有查询模型中补充 `PackageName`，让歌词路由使用 Android MediaSession 提供的播放器包名。它不改变跨端协议，只在 Windows Client 内部使用。

### ILyricsProvider

Provider 接口增加轻量的适用性判断：

- `Name`：用于 UI 展示来源。
- `CanHandle(TrackQuery)`：判断是否参与当前歌曲查询。
- `SearchAsync(...)`：返回成功时间轴或可继续兜底的失败结果。

`LyricsCoordinator` 按注册顺序遍历 Provider，跳过不适用 Provider；适用 Provider 失败后继续下一个 Provider。所有请求共享当前歌曲的取消令牌。

### QQ Music / NetEase / KuGou Provider

各在线 Provider 都拆成两个步骤：

1. 使用标题和歌手请求对应平台的搜索接口，取得有限数量的歌曲候选及其歌曲标识。
2. 对候选按标题、歌手和时长进行轻量匹配，使用最佳候选的歌曲标识请求歌词接口。
3. 解码平台返回的歌词包装，提取当前在线接口提供的 LRC 文本并复用现有解析器。

请求使用独立 `HttpClient`、明确 User-Agent/Referer、短超时和取消令牌。接口返回结构变化、HTTP 错误、编码错误和无时间轴歌词统一转换为 Provider 失败，不抛出到设备链路。平台接口属于外部兼容层，必须通过 fixture 测试隔离。

### LRCLIB Provider

保持现有实现，`CanHandle` 始终返回 true。仅将网络失败结果的来源名称从硬编码改为 Provider 自身的 `Name`，以便多来源状态准确显示。

## 路由规则

| 播放器 | Provider 顺序 |
| --- | --- |
| QQ Music (`com.tencent.qqmusic`) | QQ Music → LRCLIB |
| NetEase Cloud Music (`com.netease.cloudmusic`) | NetEase → LRCLIB |
| KuGou Music (`com.kugou.android`) | KuGou → LRCLIB |
| 其他播放器 | LRCLIB |

这里的顺序由 Provider 的 `CanHandle` 和注册顺序共同决定，后续新增来源不修改时间轴和任务栏模块。

## 失败与取消

- 当前歌曲变化时，Coordinator 取消旧歌曲的所有 Provider 请求。
- QQ Provider 失败不清空设备连接，也不影响下一次播放状态校准。
- 所有 Provider 都失败时，UI 显示最后一个有意义的失败状态，Renderer 隐藏。
- 新歌曲成功时，UI 显示实际来源名，Renderer 使用统一的 `LyricsTimeline`。

## 测试设计

- `TrackQuery` 包名路由测试。
- QQ、NetEase、KuGou 搜索响应和歌词响应的固定 JSON/编码 fixture 解析测试。
- 标题、歌手和时长匹配测试，拒绝明显错误候选。
- QQ 返回 404/超时/无同步歌词后，LRCLIB 成功的顺序测试。
- 非 QQ 播放器不调用 QQ Provider 的测试。
- 现有 LRC、Timeline、协议和 UI 构建测试保持通过。
