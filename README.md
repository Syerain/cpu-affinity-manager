# CPU Affinity Manager

Windows CPU 亲和性管理工具 — 自由控制哪个进程跑在哪些核心上。
sourcecode from bilibili@uid185415

## 它能做什么

- **绑定进程到指定核心** — 游戏绑大核，后台绑小核，数据库绑前一半
- **阻止程序自己改亲和性** — 内核级 Job Object 锁定，CPU-Z 类工具改不了
- **规则自动匹配** — 通配符匹配 + WMI 实时监控，新进程启动自动应用
- **一次写好到处跑** — 组合回退模式，同一规则在 Intel 大小核和 AMD 全大核机器上都能正确工作
- **AI 可操控** — 内置 MCP Server，Claude 等 AI 可以直接管理 CPU 亲和性

## 核心原理

### 优先级链

Windows 内核中同时存在多种 CPU 限制机制，冲突时按优先级裁决：

```
优先级 1（最高）: Job Object Affinity Limit
  ├─ 内核对象，创建者退出后仍存在
  ├─ 可覆盖进程自身设置
  └─ 目标进程无法用任何用户态 API 绕过

优先级 2: Process Affinity Mask
  └─ NtSetInformationProcess(0x15) 设置，进程存活期间有效

优先级 3: Thread Affinity Mask
  └─ 进一步收窄进程亲和性，最终调度核心 = Process ∩ Thread

优先级 4（最低）: CPU Sets（软性偏好）
  └─ 仅作为调度器的"建议"，不是强制
```

**关键结论**：只有 Job Object 能阻止目标进程自己改亲和性。

### 为什么 Process Affinity 不够

即使你用 `SeDebugPrivilege` + ntdll 设置了目标的亲和性，目标进程随时可以自己再改回来。Job Object 在内核层面拦截：目标调用 `NtSetInformationProcess` → 内核先检查 Job 限制 → 不在允许范围内则拒绝。

## 功能

### 亲和性模式

| 模式 | 说明 |
|------|------|
| `p-cores` | 仅大核 / 性能核 |
| `e-cores` | 仅小核 / 能效核 |
| `all-cores` | 全部逻辑处理器 |
| `p-cores-smt` | 大核全部线程（含超线程） |
| `p-cores-no-smt` | 仅大核物理线程 |
| `first-half` | 逻辑编号前一半 |
| `second-half` | 逻辑编号后一半 |
| `custom` | 自定义十六进制掩码 |

### 组合回退模式（自动适配不同机器）

用 `|` 串联，依次尝试，返回第一个非零结果：

```
p-cores|first-half     → 有大核用大核，没大核用前一半（写一次，通吃 Intel/AMD）
e-cores|second-half    → 有小核用小核，没小核用后一半
p-cores|e-cores|all-cores  → 三级回退
```

### Socket 过滤（多路服务器）

```
p-cores@socket0        → 第1个物理CPU的大核
all-cores@socket1      → 第2个物理CPU的全部核心
```

### 实施级别

| 级别 | 机制 | 持久性 | 防自改 | 适用场景 |
|------|------|--------|--------|---------|
| `soft-cpu-sets` | CPU Sets | 进程存活 | ❌ | 后台任务，不需要严格绑核 |
| `hard-affinity` | NtSetInformationProcess(0x15) | 进程存活 | ❌ | 普通程序 |
| `job-enforced` | Job Object | **内核对象持久** | ✅ | 会自己改亲和性的程序 |
| `job-locked` | Job Object + 禁止脱离 | **内核对象持久** | ✅ | CPU-Z 等反篡改场景 |

### 通配符规则

| 语法 | 匹配 | 示例 |
|------|------|------|
| `*` | 任意字符（不含 `\`） | `game*.exe` → `game2024.exe` |
| `**` | 跨目录任意字符 | `D:\Games\**` → 递归所有子目录 |
| `?` | 单个字符 | `app?.exe` → `app1.exe` |
| `\|` | OR 分隔 | `a.exe\|b.exe` |
| `[...]` | 字符范围 | `app[0-9].exe` |

### 规则匹配逻辑

```
对于每个进程(进程名, 完整路径):
  按顺序遍历每条规则:
    规则未启用 → 跳过
    进程名不匹配通配符 → 跳过
    指定了路径但路径不匹配 → 跳过
    匹配到排除列表 → 跳过
    → 命中！执行规则，停止匹配
```

## 快速开始

### 编译

```bash
dotnet build -c Release
```

输出在 `src/CpuAffinityManager.App/bin/Release/net10.0-windows/`。

### Avalonia Windows 发布包

在 PowerShell 中运行：

```powershell
.\scripts\publish-avalonia.ps1
```

会生成自包含的 Windows x64 NativeAOT 发布目录 `publish/avalonia-win-x64/`，其中包含可执行文件、`config/default-rules.json` 和 `docs/ai-guide.md`。

### GUI

运行 `CpuAffinityManager.App.exe`。左侧导航切换 Dashboard / Processes / Rules / Settings。

### 命令行 / AI 操控

```bash
# 启动 MCP Server（stdio 模式，供 AI Agent 使用）
CpuAffinityManager.Mcp.exe
```

MCP 工具列表：

| 工具 | 功能 |
|------|------|
| `get_topology` | 获取 CPU 拓扑（P/E核数、Socket数） |
| `list_processes` | 列出运行进程及当前亲和性 |
| `get_rules` | 查看已配置的规则 |
| `set_affinity` | 直接设置进程 CPU 亲和性 |
| `apply_rule` | 按规则 ID 应用到进程 |
| `scan_and_enforce` | 批量扫描全部进程并应用规则 |
| `add_rule` | 添加规则 |
| `remove_rule` | 删除规则 |

AI 使用指南见 [docs/ai-guide.md](docs/ai-guide.md)。

### 规则文件

规则存储在 JSON 文件中（默认 `config/default-rules.json`）：

```json
{
  "version": 2,
  "rules": [
    {
      "id": "rule-001",
      "name": "游戏绑定大核（无大核则前一半）",
      "enabled": true,
      "match": {
        "process": "*.exe",
        "path": "D:\\Games\\**",
        "exclude": ["*launcher*.exe"]
      },
      "action": {
        "type": "cpu-affinity",
        "mode": "p-cores|first-half",
        "level": "job-enforced"
      }
    }
  ]
}
```

## 场景速查

| 需求 | mode | level |
|------|------|-------|
| 游戏用大核 | `p-cores` | `job-enforced` |
| 后台更新用小核 | `e-cores\|second-half` | `soft-cpu-sets` |
| 数据库用大核（兼容AMD） | `p-cores\|first-half` | `hard-affinity` |
| 阻止CPUZ改亲和性 | `all-cores` | `job-locked` |
| 系统服务前一半核心 | `first-half` | `hard-affinity` |
| 双路服务器第1路大核 | `p-cores@socket0` | `hard-affinity` |
| 可移植规则（通吃所有机器） | `p-cores\|first-half` | `job-enforced` |

## 系统要求

- Windows 10/11（需要 WMI 和 Job Object API）
- .NET 10 Runtime
- 管理员权限（`job-enforced` 和 `job-locked` 级别）

## 项目结构

```
CpuAffinityManager/
├── config/default-rules.json          # 默认规则
├── docs/
│   ├── design.md                      # 架构设计文档
│   └── ai-guide.md                    # AI Agent 使用指南
├── src/
│   ├── CpuAffinityManager.Core/       # 核心引擎 DLL
│   ├── CpuAffinityManager.App/        # WPF GUI
│   ├── CpuAffinityManager.Avalonia/   # Avalonia GUI（跨平台）
│   └── CpuAffinityManager.Mcp/        # MCP Server
└── tests/
    └── CpuAffinityManager.Tests/      # 单元测试
```
