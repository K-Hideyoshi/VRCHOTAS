# 详细诊断日志 - 快速定位指南

## 🎯 目标
通过极其详细的日志输出,在几次测试内快速定位Overlay未显示的根本原因。

## 📋 新增日志覆盖的关键路径

### 1️⃣ **SteamVR进程检测** (OpenVrNativeLibraryService.cs)

#### IsSteamVrRunning()
```
✓ 检测到的进程: "SteamVR processes detected: vrserver(x1), vrmonitor(x1), vrcompositor(x1)"
✗ 未检测到: "No SteamVR processes detected."
```

#### IsSteamVrIpcReady()
```
=== IPC Readiness Check Started ===
vrserver process count: 1
vrserver.exe details: PID=12345, StartTime=14:23:45, Uptime=8.50s
✓ IPC Check Result: ✓ SteamVR IPC appears ready
✗ IPC Check Result: vrserver too young (2.3s < 5s), IPC may not be ready
✗ IPC Check Result: SteamVR not running
```

---

### 2️⃣ **初始化流程** (OpenVrOverlayRuntime.cs - EnsureInitialized)

#### 初始化尝试开始
```
=== Starting Initialization Attempt #1 ===
```

#### 节流检查
```
Initialization throttled, waiting 4.5s before next attempt
```

#### SteamVR运行检查
```
Init aborted: SteamVR not running
```

#### IPC就绪检查 (新增!)
```
Init aborted: SteamVR IPC not ready, will retry in 2s
```

#### 预检查通过
```
✓ Pre-checks passed: SteamVR running and IPC ready
```

#### DLL加载
```
✓ openvr_api.dll loaded successfully
OpenVR RuntimePath: 'D:\Software\Game\Steam\steamapps\common\SteamVR'
```

#### 运行时诊断
```
✓ OpenVR diagnostics: IsRuntimeInstalled=True, IsHmdPresent=True
```

---

### 3️⃣ **OpenVR.Init调用** (详细线程跟踪)

#### Init开始
```
>>> Calling OpenVR.Init(VRApplication_Overlay) with 10s safety timeout (attempt #1)...
Init task started on thread 42
Entering OpenVR.Init() native call...
```

#### Init成功
```
OpenVR.Init() returned on thread 42
<<< OpenVR.Init completed in 245.3ms
OpenVR.Init result: error=0 (No Error), CVRSystem=valid
✓✓✓ OpenVR.Init succeeded!
```

#### Init超时
```
<<< OpenVR.Init TIMED OUT after 10s (still blocked on thread 42)
```

#### Init失败
```
<<< OpenVR.Init completed in 523.8ms
OpenVR.Init result: error=310 (Shared IPC Namespace Unavailable), CVRSystem=null
✗ Init failed: Shared IPC Namespace Unavailable (code 310), retry in 5.0s
```

---

### 4️⃣ **Overlay接口和渲染器创建**

#### Overlay接口
```
Retrieving OpenVR.Overlay interface...
✓ OpenVR.Overlay interface obtained
```

#### 渲染器创建
```
Creating texture renderer (mode=Auto)...
Attempting to create D3D11OverlayTextureRenderer...
✓ D3D11OverlayTextureRenderer created successfully
✓ Renderer created: D3D11OverlayTextureRenderer
```

#### 完成
```
=== Initialization Complete: Overlay runtime ready with D3D11OverlayTextureRenderer ===
```

---

### 5️⃣ **Toast渲染流程**

```
Toast: Rendering "Test Toast Message" (dirty=True, visible=False)
Toast: Uploading texture via D3D11OverlayTextureRenderer...
Toast: ✓ Texture uploaded
Toast: Showing overlay...
Toast: ✓ Shown successfully, expires at 14:25:30
```

#### 失败场景
```
Toast: Failed to ensure overlay handle or renderer is null
Toast: Upload failed: <error message>
Toast: Failed to show overlay
```

---

### 6️⃣ **Status渲染流程**

```
Status: Rendering (dirty=True, visible=False)
Status: Uploading texture via D3D11OverlayTextureRenderer...
Status: ✓ Texture uploaded
Status: Showing overlay...
Status: ✓ Shown successfully
```

---

## 🔍 如何使用这些日志快速定位问题

### 场景1: Overlay完全不显示

**查找顺序:**
1. 搜索 `=== Starting Initialization Attempt`
   - 如果找不到 → Helper未启动或Tick循环未运行
   - 如果找到 → 继续

2. 查看是否有 `Init aborted: SteamVR IPC not ready`
   - 如果持续出现 → SteamVR未就绪,等待5秒后重试
   - 如果没有 → 继续

3. 查找 `>>> Calling OpenVR.Init`
   - 如果找不到 → IPC检查一直失败
   - 如果找到 → 查看返回结果

4. 查找 `OpenVR.Init result: error=X`
   - `error=0` → Init成功,继续下一步
   - `error!=0` → Init失败,查看错误码和描述

5. 查找 `✓✓✓ OpenVR.Init succeeded!`
   - 如果找到 → 查看渲染器创建
   - 如果没有 → Init失败

6. 查找 `Toast: Rendering` 或 `Status: Rendering`
   - 如果找不到 → Toast/Status从未触发
   - 如果找到 → 查看是否有错误

7. 查找 `✓ Shown successfully`
   - 如果找到 → Overlay已显示,检查VR中是否真的看不见
   - 如果找不到 → 查看失败原因

---

### 场景2: Init阻塞/超时

**特征:**
```
>>> Calling OpenVR.Init...
Init task started on thread 42
Entering OpenVR.Init() native call...
... (长时间没有输出)
<<< OpenVR.Init TIMED OUT after 10s
```

**原因:** IPC检查不够准确,或SteamVR在启动后5秒内IPC仍未就绪

**解决:** 
- 检查 `IPC Readiness Check` 日志
- 查看vrserver进程启动时间
- 考虑增加IPC就绪判断的等待时间(目前是5秒)

---

### 场景3: Init成功但Overlay不显示

**特征:**
```
✓✓✓ OpenVR.Init succeeded!
=== Initialization Complete: Overlay runtime ready with D3D11OverlayTextureRenderer ===
```

但没有 `Toast: Rendering` 或 `Status: Rendering`

**原因:** 
- Master开关未开启
- Status Indicator未启用
- Toast消息为空或未触发

**解决:** 
- 检查UI状态
- 手动触发 "Show test toast"
- 检查配置文件是否正确

---

### 场景4: 渲染器创建失败

**特征:**
```
Attempting to create D3D11OverlayTextureRenderer...
D3D11 renderer creation failed (Auto mode), falling back to raw: ...
Using RawOverlayTextureRenderer (compatibility mode)
```

**原因:** Direct3D11初始化失败(SharpDX问题或GPU不支持)

**解决:** 
- 检查SharpDX DLL是否存在
- 查看详细异常堆栈
- 使用Raw模式作为fallback

---

## 📊 典型成功流程的完整日志示例

```
=== Starting Initialization Attempt #1 ===
=== IPC Readiness Check Started ===
SteamVR processes detected: vrserver(x1), vrmonitor(x1), vrcompositor(x1)
vrserver process count: 1
vrserver.exe details: PID=12345, StartTime=14:23:45, Uptime=8.50s
IPC Check Result: ✓ SteamVR IPC appears ready
✓ Pre-checks passed: SteamVR running and IPC ready
✓ openvr_api.dll loaded successfully
OpenVR RuntimePath: 'D:\Software\Game\Steam\steamapps\common\SteamVR'
✓ OpenVR diagnostics: IsRuntimeInstalled=True, IsHmdPresent=True
>>> Calling OpenVR.Init(VRApplication_Overlay) with 10s safety timeout (attempt #1)...
Init task started on thread 42
Entering OpenVR.Init() native call...
OpenVR.Init() returned on thread 42
<<< OpenVR.Init completed in 245.3ms
OpenVR.Init result: error=0 (No Error), CVRSystem=valid
✓✓✓ OpenVR.Init succeeded!
Retrieving OpenVR.Overlay interface...
✓ OpenVR.Overlay interface obtained
Creating texture renderer (mode=Auto)...
Attempting to create D3D11OverlayTextureRenderer...
✓ D3D11OverlayTextureRenderer created successfully
✓ Renderer created: D3D11OverlayTextureRenderer
=== Initialization Complete: Overlay runtime ready with D3D11OverlayTextureRenderer ===
Toast: Rendering "Test Toast Message" (dirty=True, visible=False)
Toast: Uploading texture via D3D11OverlayTextureRenderer...
Toast: ✓ Texture uploaded
Toast: Showing overlay...
Toast: ✓ Shown successfully, expires at 14:25:30
```

---

## ⚡ 快速诊断检查清单

运行一次测试后,按顺序检查:

- [ ] `SteamVR processes detected` - SteamVR是否运行?
- [ ] `IPC Check Result: ✓` - IPC是否就绪?
- [ ] `✓ Pre-checks passed` - 预检查是否通过?
- [ ] `<<< OpenVR.Init completed` - Init是否完成(未超时)?
- [ ] `error=0 (No Error)` - Init是否成功?
- [ ] `✓✓✓ OpenVR.Init succeeded!` - Init结果确认
- [ ] `✓ Renderer created` - 渲染器是否创建?
- [ ] `Toast: Rendering` - Toast是否触发?
- [ ] `✓ Shown successfully` - Overlay是否显示?

**任何一步失败,立即查看该步骤的详细日志和错误信息!**

---

## 🎉 预期效果

通过这些详细日志,你应该能在**2-3次测试**内:
1. 精确定位失败发生在哪个环节
2. 获取详细的错误上下文(线程ID、耗时、错误码)
3. 排除不相关的可能性
4. 快速验证修复是否有效
