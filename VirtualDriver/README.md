# VirtualDriver (OpenVR)

## 依赖
- Visual Studio 2022/2026 (MSVC v143+)
- CMake 3.20+
- OpenVR SDK

## 构建步骤
1. 打开 `x64 Native Tools Command Prompt for VS 2026`，或先执行：
   - `"%ProgramFiles%\Microsoft Visual Studio\2026\Community\VC\Auxiliary\Build\vcvars64.bat"`
2. 配置 CMake：
   - `cmake -S . -B build -G "Visual Studio 18 2026" -A x64 -DOPENVR_SDK_PATH=D:/sdk/openvr`
3. 编译：
   - `cmake --build build --config Release`
4. 将生成的 `driver_vrchotas.dll` 与 OpenVR 驱动清单一起部署到 SteamVR 驱动目录。

## 运行时通信
- 共享内存名：`Local\\VRCHOTAS.VirtualController.State`
- 互斥体名：`Local\\VRCHOTAS.VirtualController.State.Mutex`
- 数据结构：`include/virtual_controller_state.h`（需与 C# 完全一致）
