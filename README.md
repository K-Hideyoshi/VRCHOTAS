# VRCHOTAS

本仓库采用 Hybrid Architecture：
- `ControllerApp`（当前 `VRCHOTAS` WPF 项目）：读取 HOTAS 输入并写入共享内存。
- `VirtualDriver`（C++ OpenVR Driver）：每帧读取共享内存并更新虚拟控制器。

## 分阶段落地状态
1. **第一阶段 IPC**：已完成
   - C#/C++ 共享结构体 `VirtualControllerState`
   - MemoryMappedFile + Mutex 写入（C#）
   - FileMapping + Mutex 读取（C++）
2. **第二阶段 驱动骨架**：已完成
   - `HmdDriverFactory`
   - 左右手控制器注册
   - `RunFrame()` 读共享内存并更新输入
3. **第三阶段 输入与映射引擎**：已完成基础实现
   - `JoystickService`（SharpDX.DirectInput）
   - `MappingEngine`（死区/曲线/反向）
   - `ConfigurationService`（Newtonsoft.Json）
4. **第四阶段 WPF UI**：已完成基础实现
   - MVVM 绑定
   - 实时轴/按键监视
   - 动态映射条目与测试模式

## 后续建议
- 为 OpenVR 输入组件补齐完整 profile（触发器、摇杆、抓握等）。
- 增加设备断线自动重连策略与 UI 告警。
- 增加映射配置版本号与迁移逻辑。
