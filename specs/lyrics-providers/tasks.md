# 多来源歌词 Provider 实施任务

- [x] 1. 扩展查询模型与 Provider 契约
  - 为 `TrackQuery` 增加播放器包名。
  - 为 `ILyricsProvider` 增加适用性判断。
  - 让 `LyricsCoordinator` 按适用性和注册顺序执行并继续兜底。
  - _需求：R1、R3_

- [x] 2. 实现 QQ Music、NetEase、KuGou Provider
  - 分别实现歌曲候选搜索、轻量匹配和歌词请求。
  - 复用现有 LRC 解析器，处理各在线接口的编码包装，不引入重量级依赖。
  - 将网络、编码和格式失败转换为可兜底结果。
  - _需求：R2、R5_

- [x] 3. 接入 Windows Client
  - 注册播放器 Provider 和 LRCLIB Provider。
  - 让 UI 显示最终命中的真实来源。
  - _需求：R1、R3、R4_

- [x] 4. 补充自动化测试
  - 增加各平台路由、响应解析、候选匹配和兜底测试。
  - 运行 Windows Core 测试、Windows Client 编译和 Android APK 构建。
  - _需求：R5_

- [ ] 5. 完成真实设备验收
  - QQ 音乐、网易云音乐、酷狗音乐各验证一首可查询歌曲。
  - 验证在线 Provider 失败后仍可使用 LRCLIB。
  - 验证无歌词时连接保持。
  - _需求：R5_
