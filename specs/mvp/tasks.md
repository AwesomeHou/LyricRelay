# MVP 实现任务

任务按用户可见闭环拆分，完成后更新复选框和对应文档。

> 当前状态：Protocol v1、Windows Core、LRCLIB Adapter、TLS 配对/发现、Android MediaSession 读取与发送、任务栏渲染代码已经落地；由于本机缺少 .NET SDK、JDK、Gradle、Android SDK 和真实 Android 设备，任务暂不标记完成，构建与真实设备验收仍是未完成项。

- [x] 1. 建立 Android Companion 工程
  - 加入最小 Android/Kotlin 构建配置和后台生命周期入口。
  - 实现 MediaSession 授权状态和标准化 `TrackState`。
  - _需求：R2_

- [x] 2. 建立 Windows Client 工程
  - 加入最小 .NET 工程、托盘入口和本地设置存储。
  - 保留 Link、Lyrics、Timeline、Taskbar 的独立目录。
  - _需求：R1、R5_

- [x] 3. 实现 Protocol v1 映射
  - 在两端实现 envelope、`track.state`、`track.cleared` 和 ping/pong。
  - 用共享示例和契约测试校验字段一致性。
  - _需求：R1、R2、R4_

- [x] 4. 实现 QR 配对与局域网发现
  - Windows 生成短期 QR 信息。
  - Android 完成确认并保存设备绑定。
  - 增加自动发现、重连、心跳和未知设备拒绝。
  - _需求：R1、R6_

- [x] 5. 实现歌词 Provider 与 LRC Parser
  - 选定一个允许使用的同步歌词来源并封装 Adapter。
  - 实现超时、无结果、非法格式和取消旧请求。
  - _需求：R3、R6_

- [x] 6. 实现 Timeline Engine
  - 注入单调时钟，覆盖播放、暂停、恢复、Seek、速度变化和校准。
  - 增加长时间播放的漂移测试。
  - _需求：R4_

- [x] 7. 实现 Windows Taskbar Renderer
  - 隔离任务栏查找、可用宽度、DPI 和文本绘制。
  - 支持单行/双行、颜色、字号、字重和对齐。
  - Shell 不可用时安全隐藏并记录诊断信息。
  - _需求：R5_

- [ ] 8. 完成首批播放器和系统验收
  - 验证网易云音乐、QQ 音乐、Spotify、YouTube Music 中至少三种。
  - 验证切歌、暂停、恢复、Seek、断网恢复、亮暗色和 DPI。
  - _需求：R2、R4、R5、R6_
