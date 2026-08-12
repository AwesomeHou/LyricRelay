# Android Companion

Android 伴侣 App 不负责播放音乐，只负责读取当前活跃 MediaSession，并把最小必要的播放状态发送给已配对的 Windows Client。

## 计划模块

```text
src/
├─ media/       # MediaSession 读取与播放器兼容处理
├─ link/        # 配对、发现、连接和重连
├─ protocol/    # packages/protocol 的 Android 映射
└─ app/         # 生命周期、权限和后台运行入口
tests/          # MediaSession 映射、状态去重、协议和重连测试
```

## 责任

- 获取 `title`、`artist`、`album`、`duration`、`packageName`。
- 获取 `playing`、`paused`、`position`、`playbackSpeed`。
- 切歌、播放、暂停、Seek 时立即发送状态。
- 播放中按校准周期发送进度，不发送高频逐帧进度。
- 通过系统授权读取 MediaSession；不读取或传输音频内容。

## 非责任

- 播放、暂停或控制第三方播放器。
- 请求 AudioPlaybackCapture。
- 下载或解析歌词。
- 保存播放历史或建立用户账号。

实现时优先保持后台服务和权限面最小，详细边界见 [架构](../../docs/architecture.md) 与 [安全说明](../../docs/security.md)。

