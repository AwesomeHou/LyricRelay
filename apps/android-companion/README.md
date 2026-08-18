# Android Companion

Android 伴侣 App 不负责播放音乐，只负责读取当前活跃 MediaSession，并把最小必要的播放状态发送给已配对的 Windows Client。

## 工程结构

```text
app/src/main/java/com/lyricrelay/companion/
├─ MainActivity.kt
├─ MediaNotificationListener.kt  # MediaSession 读取
├─ RelayService.kt               # 前台服务、校准发送和重连边界
├─ LinkClient.kt                 # TLS 链路和协议消息
├─ DiscoveryClient.kt            # 局域网自动发现
├─ PairingStore.kt               # Android Keystore 保护的本地配对信息
└─ ProtocolModels.kt             # Protocol v1 的 Android 映射
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

## 本地构建

需要 JDK 17、Android SDK 35 和 Gradle 8.7 或兼容版本。当前仓库验证使用 `E:\LyricRelay\.tools\android` 下的本地工具链：

```powershell
gradle -p apps/android-companion :app:assembleDebug
```

Android Debug APK 已在本机成功构建；当前 `adb devices -l` 没有连接实体设备，因此 MediaSession、后台保活、真实播放器和双端链路仍未完成真实设备验收。
