# LyricRelay MVP 技术设计

## 技术选择

| 部分 | 选择 | 原因 |
| --- | --- | --- |
| Android | Kotlin + 系统 MediaSession | 直接读取系统播放状态，减少播放器耦合 |
| Windows | C# / .NET + Win32 互操作 | 业务逻辑易测试，任务栏能力集中隔离 |
| 跨端协议 | 版本化 JSON 消息 | 便于两端独立迭代，避免早期代码生成 |
| 连接 | 局域网发现 + 长连接 | 满足自动连接与实时状态推送 |
| 同步 | Windows 本地单调时钟 | 减少手机进度消息和网络抖动影响 |

## 组件关系

```text
Android
  MediaSessionReader → StatePublisher → DeviceLink
                                      ↓
Windows
  DeviceLink → LyricsCatalog → LyricsParser → TimelineEngine → TaskbarRenderer
                                            ↑              ↑
                                       Track Metadata   PlaybackState
```

协议字段和消息类型以 [Protocol v1](../../packages/protocol/README.md) 为准。

## 关键数据模型

### TrackState

```text
trackId: string
title: string
artist: string?
album: string?
durationMs: int?
packageName: string?
state: playing | paused | stopped
positionMs: int
playbackSpeed: float
stateVersion: long
```

### TimedLine

```text
startMs: int
text: string
```

Parser 输出按 `startMs` 升序排列；Timeline Engine 根据当前毫秒位置选择最后一个 `startMs <= position` 的非空行。

## 状态策略

- Android 负责识别事件和发送校准点，不负责决定当前歌词行。
- Windows 以 `trackId` 串联一首歌的请求、时间轴和渲染状态。
- 所有异步请求带取消标记和 `trackId`，结果提交前再次校验当前歌曲。
- 链路恢复后必须发送完整状态，不能只发送“已连接”。

## 配对策略

Windows 生成短期 QR 信息，内容包含临时地址、端口、协议版本、Windows 设备 ID、一次性 token 和服务器证书指纹。Android 扫描后回传确认；确认成功后双方保存设备绑定信息。服务发现只用于后续找到设备，不能替代凭据校验。

实现时必须补齐具体加密/认证方案，并遵守 [安全与隐私](../../docs/security.md) 的已知限制说明。

## 歌词 Provider 策略

定义最小 Provider 接口：

```text
search(TrackQuery, CancellationToken) -> LyricsResult
```

Provider 负责网络请求、来源字段和原始格式转换；上层只接收 `TimedLine[]` 或可区分的失败原因。MVP 接入 QQ 音乐、网易云、酷狗和 LRCLIB 四个在线来源，不支持本地歌词文件，不改变 Timeline Engine。

## 任务栏策略

TaskbarRenderer 对外只接受：当前行、可选下一行、显示设置和任务栏几何信息。Shell 查找、UI Automation、DPI 和 DirectWrite/Direct2D 都放在实现内部；任何 Shell 失败都返回不可用状态并安全隐藏。

## 测试设计

- Timeline、Parser、协议解析使用纯单元测试。
- Link 使用本机模拟端覆盖配对、心跳和重连。
- Provider 使用固定响应夹具，不在单元测试中访问真实服务。
- Renderer 先测试布局计算和降级逻辑，再做少量 Windows 手工验证。
