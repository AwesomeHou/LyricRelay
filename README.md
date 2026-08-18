# LyricRelay

> 手机听，电脑看。

LyricRelay 是一个连接 Android 手机与 Windows PC 的轻量任务栏歌词工具。用户继续使用手机原有音乐播放器和蓝牙耳机，LyricRelay 通过 Android MediaSession 获取当前歌曲与播放进度，在 Windows 任务栏显示自动匹配的时间轴歌词。

## MVP

MVP 只验证一件事：

> 手机在听什么，电脑任务栏就同步显示什么歌词。

包含两个客户端：

- Android Companion：读取当前活跃 MediaSession，发送歌曲 Metadata 与 PlaybackState。
- Windows Client：自动连接手机，获取同步歌词，计算时间轴，并渲染到 Windows 任务栏。

不传输手机音频，不改变用户原有的播放器和耳机使用方式。

## 当前状态

当前仓库已落地 MVP 的首版客户端源代码：Android MediaSession 读取与 TLS 发送、Windows TLS 配对/发现、QQ 音乐/网易云/酷狗/LRCLIB 在线 Provider、LRC/时间轴核心和任务栏渲染均已进入工程。Windows Core 测试、Windows Client 编译和 Android Debug APK 构建已通过；当前已用 Android 实体设备完成配对、热点网络连接和 QQ 音乐 MediaSession 状态传输验证。完整 MVP 仍需用实体设备播放可命中歌词的歌曲完成任务栏歌词显示，并继续验证 Shell、Seek、长时间漂移和更多播放器。

## 仓库结构

```text
LyricRelay/
├─ apps/
│  ├─ android-companion/       # Android 伴侣 App
│  └─ windows-client/          # Windows 客户端
├─ packages/
│  └─ protocol/                # Android ↔ Windows 的语言无关协议
├─ docs/                       # 架构、开发、测试、安全与路线图
├─ specs/
│  └─ mvp/                     # MVP 需求、设计和实现任务
├─ AGENTS.md                  # 项目协作约定
├─ .editorconfig
├─ .gitattributes
└─ .gitignore
```

## 推荐技术路线

- Android：Kotlin，优先使用系统 MediaSession 能力。
- Windows：C# / .NET，使用 Win32 互操作隔离任务栏渲染。
- 链路：同一局域网内的设备发现 + 长连接 JSON 消息。
- 歌词：Windows 侧 Provider Adapter，当前接入 QQ 音乐、网易云音乐、酷狗和 LRCLIB 四个在线来源；暂不支持本地歌词文件。
- 核心同步：Windows 本地时间轴引擎，手机只发送状态变化和周期性校准点。

具体边界见 [架构文档](docs/architecture.md)，消息格式见 [协议文档](packages/protocol/README.md)。

## 文档入口

- [文档索引](docs/README.md)
- [MVP 需求](specs/mvp/requirements.md)
- [MVP 技术设计](specs/mvp/design.md)
- [MVP 实现任务](specs/mvp/tasks.md)
- [开发与编程规范](docs/development.md)
- [测试策略](docs/testing.md)
- [安全与隐私](docs/security.md)
- [路线图](docs/roadmap.md)
- [Codex MVP 目标模式提示词](docs/codex-mvp-goal-prompt.md)

## 本地开发

客户端构建命令分别记录在 [Android Companion README](apps/android-companion/README.md) 和 [Windows Client README](apps/windows-client/README.md) 中；根目录不复制可能失效的命令。

## MVP 成功标准

- 播放歌曲后，Windows 无需用户操作即可出现歌词。
- 切歌、暂停、恢复和 Seek 能快速反映到任务栏。
- 长时间播放无明显累计漂移。
- 同一局域网短暂抖动恢复后，连接和歌词状态可以自动恢复。
- 用户仍然使用原有 Android 播放器与耳机，产品不要求改变听歌习惯。
