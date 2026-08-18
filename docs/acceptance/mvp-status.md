# MVP 验收状态

更新时间：2026-08-18

最近一次自动化验证：Windows Core 测试通过（17/17）、QQ Music 与 KuGou 在线 Provider 检查通过、Windows Client 编译通过（0 错误；NuGet 漏洞审计因网络不可用产生 1 条环境警告）、Android Debug APK 构建通过（仅 Camera2 API 弃用警告）。任务栏渲染器已切换为 TaskbarLyrics 风格的 `Shell_TrayWnd` 子窗口 + UI Automation 可用间隙计算，并保留 Win32 回退路径；最终运行实例已监听 47250，手机热点 TCP 连接处于 `ESTABLISHED`，QQ Music 当前异常 Metadata 已由 QQ Adapter 成功命中同步歌词。真实视觉验收仍待用户确认。

状态含义：

- `静态已满足`：当前源代码或配置直接提供了对应行为，但没有替代真实运行验证。
- `待验证`：实现路径已存在，需要构建、设备或 Windows Shell 运行证据。
- `部分验证`：链路已真实运行，但当前场景受歌词源数据或外部环境限制，尚不能视为完整通过。
- `未完成`：当前没有足够实现或验收证据，不能宣称通过。

## 工具链门槛

| 项目 | 当前状态 | 证据 |
| --- | --- | --- |
| .NET SDK | 本地已安装 | `E:\LyricRelay\.tools\dotnet-full\dotnet.exe`，8.0.424 |
| JDK | 本地已安装 | `E:\LyricRelay\.tools\android\jdk`，用于 Android 构建 |
| Gradle | 本地已安装 | `E:\LyricRelay\.tools\android\gradle`，8.7 |
| Android SDK / adb | 本地已安装 | `E:\LyricRelay\.tools\android\sdk`，已连接 Android 16 实体设备 |
| Android 实体设备 | 已连接 | 当前已安装 Debug APK；MediaSession 授权已启用 |
| Windows Shell 运行验证 | 部分执行 | 最终 Windows Client 已启动并与 Android 保持 TCP `ESTABLISHED`；QQ Provider 返回 91 行同步歌词，UI Automation 已计算出可用任务栏区域，仍需用户确认最终文字可见 |

## Android 验收

| ID | 状态 | 实现/证据 |
| --- | --- | --- |
| A-01 | 静态已满足 | `MainActivity` 提供 MediaSession 授权入口；APK 已成功构建 |
| A-02 | 已验证 | MediaSession 授权已启用，QQ Music 会话可被读取 |
| A-03 | 已验证 | 实机读取 QQ Music Metadata/PlaybackState，播放状态为 PLAYING |
| A-04 | 已验证 | QQ Music 切换/播放状态通过 MediaController callback 和状态广播进入 RelayService |
| A-05 | 已验证 | 实机选中 QQ Music 播放会话，位置随单调时间推进 |
| A-06 | 静态已满足 | 无会话发送 cleared，旧状态不继续作为当前状态 |
| A-07 | 静态已满足 | Manifest 和源代码无 AudioPlaybackCapture、麦克风或音频录制 |
| A-08 | 静态已满足 | `RelayService` 默认 2 秒校准，不使用逐帧进度消息 |
| A-09 | 已验证 | 实机连接成功；RelayService 单线程发送、失败关闭和周期重试路径已运行 |
| A-10 | 静态已满足 | `PairingStore` 使用 Android Keystore AES-GCM |

## Windows 验收

| ID | 状态 | 实现/证据 |
| --- | --- | --- |
| W-01 | 静态已满足 | QRCoder、2 分钟有效期、token 一次性消费 |
| W-02 | 静态已满足 | TLS 指纹 pinning、协议版本、设备 ID、设备密钥校验 |
| W-03 | 已验证 | 手机热点下 Android 已连接 Windows TCP 47250，连接状态显示“设备已连接” |
| W-04 | 待验证 | HKCU Run 开机启动、AutoConnect 设置和后台参数已实现 |
| W-05 | 部分验证 | Windows 重启后 Android 可自动恢复连接；旧版 APK 在热点网络下观察到 Established，节流修复版待安装后复测 |
| W-06 | 静态已满足 | `LyricsCoordinator` 取消旧请求并以 trackId 隔离结果 |
| W-07 | 部分验证 | Renderer 将无焦点子窗口设为 Shell_TrayWnd 子窗口；运行日志已确认布局计算成功，仍需用户确认可见 |
| W-08 | 已验证 | 优先用 UI Automation 寻找安全间隙，Win32 子窗口枚举作为回退；当前任务栏已计算出 322px 可用区域 |
| W-09 | 待验证 | 支持水平/垂直任务栏；无安全间隙时隐藏 |
| W-10 | 待验证 | 使用 `GetDpiForWindow` 按 DPI 缩放 |
| W-11 | 待验证 | 颜色、亮暗色可读性和隐藏行为需真实 Shell 验证 |
| W-12 | 静态已满足 | 设置 UI 覆盖开机启动、自动连接、单双行、字体、字号、字重、颜色、对齐、偏移 |
| W-13 | 静态已满足 | Shell 布局失败仅隐藏 Renderer |
| W-14 | 静态已满足 | Windows DPAPI 保护配对设备文件，日志不输出密钥 |

## 端到端验收

| ID | 状态 | 说明 |
| --- | --- | --- |
| E2E-01 | 已验证 | Windows QR、Android 扫码、权限授权和配对已完成 |
| E2E-02 | 已验证 | 手机热点网络下双端连接已建立；无需同一家庭 Wi-Fi |
| E2E-03 | 部分验证 | 当前 QQ Music MediaSession 正在播放且状态链路已连接；QQ Provider 已通过异常 Metadata 拆分兜底返回同步歌词，仍待确认任务栏最终可见 |
| E2E-04 | 未完成 | 未完成真实 Windows 任务栏观察 |
| E2E-05 | 待验证 | Timeline Engine 已有暂停测试 |
| E2E-06 | 待验证 | Timeline Engine 已有恢复路径，未运行 |
| E2E-07 | 待验证 | Timeline Engine 已有 Seek rebase 测试，未运行 |
| E2E-08 | 待验证 | trackId 取消旧歌词请求已实现，未运行 |
| E2E-09 | 未完成 | 尚未完成三种真实播放器验证 |
| E2E-10 | 部分验证 | Windows 客户端重启后 Android 可自动恢复连接；新 APK 的发送节流修复待安装后复测 |
| E2E-11 | 静态已满足 | Provider 失败不终止设备链路，Renderer 可隐藏 |
| E2E-12 | 未完成 | 尚未执行 30 分钟真实播放和漂移测量 |
| E2E-13 | 未完成 | 尚未完成至少三种首批重点播放器验证 |

## 自动化测试

Windows Core 测试入口：

```powershell
dotnet run --project apps/windows-client/tests/LyricRelay.Core.Tests.csproj
```

测试覆盖 LRC 多标签/排序/offset、播放速度、暂停、Seek、旧版本、duration clamp、当前行、Protocol JSON 往返、四个 Provider 路由/解析和兜底，以及 QQ Music 异常 Metadata 拆分。当前已执行并通过 17 项 Windows Core 测试；QQ Music 与 KuGou 在线检查通过，Windows Client 和 Android Debug APK 已成功构建。仍缺少 Windows Shell 实际歌词显示确认和真实播放器验收。

## 完成结论

当前结论：**MVP 未完成，目标保持 active。**

解除当前验证阻塞后，必须先运行自动化测试和双端构建，再完成 Android 实体设备、Windows 10/11 Shell、DPI/亮暗色、断网恢复以及至少三种 Android 播放器的真实验收。所有 `未完成` 和 `待验证` 项关闭后，才允许将目标标记为 complete。
