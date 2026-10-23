# 验收记录

日期：2026-10-23　环境：Windows 11（本机 32 GB）、ThinkBook Toolkit 1.0.4、.NET 9.0.317

## 已通过

| 项 | 命令 | 结果 |
|---|---|---|
| 单元/契约测试 | `Tests.dll dist\tbt.memory.cleaner` | **14/14 通过**（采样、区域掩码、设置解析与钳制、非提权错误路径、自动清理策略、清单校验） |
| 真实插件宿主 | `HostSim.dll dist\tbt.memory.cleaner` | **PASS**：握手 `TBT_PLUGIN_1`，5 轮 tick 均成功，3 个传感器值有限，概览摘要存在，首轮 59 ms / 后续 0 ms |
| 清单（装机版自身校验） | `ManifestCheck.dll dist\tbt.memory.cleaner` | **PASS**：反射调用 1.0.4 的 `ToolkitPluginManager.ValidateManifest`，清单被接受 |
| 页面渲染 | `UiRender.dll out\render` | **PASS**：5 种视口/主题/语言组合非空白，54 个文本节点；点击钩子与非提权提示、区域勾选写设置均通过 |
| 指纹稳定 | HostSim 前后对插件目录做 SHA256 | **一致**（`A6C1DF87…`），运行期零写入 |
| 宿主滚动（回归） | UiRender 模拟 Toolkit 的 `_mainScroll` 后触发滚轮 | **PASS**：修复前 0→0（页面吃事件），修复后 0→48 px |
| 打包闸门 | `pack.ps1` | deps.json 无 `ThinkBookToolkit.`、逻辑 deps 含 Core、无 pdb/runtimeconfig |

渲染产物：`out/render/dark-1280.png`、`light-1280.png`、`dark-900.png`、`light-en-1280.png`、`dark-tall.png`。

打包产物：`dist/tbt.memory.cleaner-Release.zip`，SHA256 `EBB20B58…3C66AF`。

## 待真机验证（需要用户在 Toolkit 内操作）

| 项 | 判据 |
|---|---|
| 插件被发现并可通过权限审核 | 插件页出现「内存清理」，权限提示为「自绘页面（进程内）」 |
| 页面在真实宿主内渲染 | 导航「内存清理」可打开，深浅色切换正常 |
| 传感器并入宿主 | 「内存与硬盘」卡片出现 3 个读数，概览出现「空闲 / 待机」行 |
| 提权路径 | 页面顶部无「未以管理员运行」提示；点「立即清理」返回 OK |
| 真实清理效果 | 待机缓存下降、空闲内存上升；页面证据表数值与资源监视器一致 |
| 自动清理 | 阈值临时调到 50% 或在负载高时观察工作进程后台执行 |
| 卸载 | 插件页卸载后目录移除、导航消失 |

## 真机 E2E（2026-10-23，TBT 1.0.4，v1.4.0）

用管理员权限的 UIAutomation 脚本驱动真实 Toolkit（普通权限的 UIA 会被 UIPI 挡住）：

| 项 | 结果 |
|---|---|
| 页面加载 / 构成条 / 进程列表 | 正常，162 个可访问元素 |
| 手动清理 | `完成 · 3 个区域 · 0.07 s`，证据表填入 6.24/18.01/6.82/0.38 |
| 系统通知 | 右下角弹出，来源 ThinkBookToolkit，内容「空闲 -0.01 GB，待机 +0.04 GB」 |
| 单进程裁剪 | 目标进程显示「工作集已裁剪」，整体工作集 6.25 → 4.82 GB |
| 概览传感器 | 「空闲 / 待机」+ 可用内存 / 内存占用 / 待机缓存 均出现 |
| 待机触发 | **发现缺陷**：`MemoryPurgeLowPriorityStandbyList` 在本机 6.82 → 6.82 GB（零效果），全清 6.82 → 0.00 GB、空闲 18.17 → 25.05 GB |

结论：低优先级待机只占很小一块，**待机触发必须配合「待机列表（全清）」才有意义**；已在 v1.4.1 改为按勾选使用全清，并加「清完没降就退避 10 分钟」与界面提示。

## 已知坑

- **不要在自定义页面里再套 ScrollViewer**。Toolkit 把页面放进主滚动区（`_mainScroll`），内容被以无限高度测量；内层 ScrollViewer 的 `ScrollableHeight` 恒为 0，却仍会把 `MouseWheel` 标记为已处理，表现为「鼠标滚不动页面」。页面只提供内容，滚动交给宿主。UiRender 的 `interaction: host scroll` 用例守着这条。

## 已知限制

- 本地测试会话非管理员，`LiveTest` 的实机清理需另行提权执行（`whoami /priv` 无 `SeProfileSingleProcess`）。
- 待机列表/已修改页的清理会造成短暂卡顿，默认关闭且自动清理永不使用。
