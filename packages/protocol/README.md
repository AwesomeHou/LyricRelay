# LyricRelay Link Protocol v1

这是 Android Companion 与 Windows Client 之间的语言无关消息契约。MVP 采用同一局域网内的长连接传输；具体传输库由各端决定，消息内容保持 JSON 兼容。

## 设计原则

- Android 发送事实：当前歌曲和最近一次已确认的播放状态。
- Windows 推导状态：当前显示行和下一次 UI 刷新的时间点。
- 不发送音频、不发送歌词正文、不发送用户账号信息。
- 状态变化立即发送；播放中按校准周期发送，不做高频进度流。
- 消息必须可重复处理，接收端用 `messageId` 或状态版本避免旧消息覆盖新状态。

## 消息封装

```json
{
  "version": 1,
  "type": "track.state",
  "messageId": "01J...",
  "deviceId": "android-4f7c...",
  "sentAt": "2026-08-12T10:00:00Z",
  "payload": {}
}
```

`sentAt` 只用于诊断和排序辅助，不用于计算播放进度。播放进度必须以 Windows 收到消息的单调时间作为本地基准。

## MVP 消息类型

| 类型 | 方向 | 用途 |
| --- | --- | --- |
| `link.hello` | 双向 | 协商协议版本和设备能力 |
| `pairing.confirm` | 双向 | QR 配对确认和设备绑定 |
| `track.state` | Android → Windows | 歌曲 Metadata 与播放状态 |
| `track.cleared` | Android → Windows | 当前没有可用 MediaSession |
| `link.ping` / `link.pong` | 双向 | 存活检测和延迟诊断 |

## `track.state` payload

```json
{
  "trackId": "stable-id-or-fingerprint",
  "title": "晴天",
  "artist": "周杰伦",
  "album": "叶惠美",
  "durationMs": 269000,
  "packageName": "com.netease.cloudmusic",
  "state": "playing",
  "positionMs": 80520,
  "playbackSpeed": 1.0,
  "stateVersion": 42
}
```

字段约束：

- `title` 必填；`artist`、`album`、`packageName` 可以为空但应尽量提供。
- `durationMs` 未知时为 `null`；`positionMs` 不得小于 0。
- `state` 取 `playing`、`paused`、`stopped`。
- `playbackSpeed` 缺失时按 `1.0` 处理；非正数视为无效消息。
- 同一歌曲的 `stateVersion` 单调递增；切歌时重新开始计数或使用新的 `trackId`。

## 同步约定

1. Android 在切歌、播放、暂停、Seek 时立即发送 `track.state`。
2. Android 在持续播放时默认每 2 秒发送一次校准状态；该值是实现参数，不是 UI 帧率。
3. Windows 收到 `playing` 后，以收到消息的单调时间为 `t0`，按 `positionMs + elapsed × playbackSpeed` 推算当前位置。
4. 收到暂停或 Seek 状态时立即重置本地基准。
5. 收到新 `trackId` 时取消旧歌词请求，旧请求返回后不得覆盖新歌曲。
6. 断线重连后，Android 发送一份完整 `track.state`，Windows 以此重新校准。

完整示例见 [track-state.json](examples/track-state.json)。协议变更必须增加版本说明，并同时更新 Android、Windows 和测试文档。

