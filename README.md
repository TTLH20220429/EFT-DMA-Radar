# Moulman's EFT DMA 雷达

基于 [Moulman's EFT DMA Radar](https://github.com/moulmandev/EFT-DMA-Radar) 的分支版本，进行汉化支持。

## 免责声明 ⚠️
本应用已在 🪟 Windows 11 25H2（游戏端）和 🪟 Windows 11 23H2（雷达端）环境下测试通过。
⚠️ 旧版 Windows 系统（如 Windows 10）可能无法正常运行，且不提供官方支持。

**注意**：目前所有测试均基于雷达端与游戏端均设置为 **1920x1080** 分辨率的场景。

## 功能特性 ✨

- 🛰️ ESP Fuser DX9 悬浮层（透视功能）
- 🎯 设备级自动瞄准 / Kmbox 集成
- 🕵️‍♂️ 静默瞄准（内存瞄准）
- 💪 无后坐力、无枪口晃动、无限耐力
- 🧼 简洁界面

## 常见问题 ⚠️

### DX 悬浮层/D3DX 错误（"DX overlay init failed"、"ESP DX init failed: System.DllNotFoundException: 无法加载 DLL 'd3dx943.dll'..."）

若出现以下错误提示：
This app has been tested on 🪟 Windows 11 25H2 (Game) and 🪟 Windows 11 23H2 (Radar).
⚠️ Older versions of Windows (e.g., Windows 10) may not work properly and are not officially supported.

这表明你的电脑**缺少**必要的旧版 DirectX 9 *D3DX* 运行时组件（具体为 `d3dx9_43.dll`）。现代 Windows 系统（Windows 10/11）**默认不包含**该文件。

**修复方法**：

1. 在你的 **雷达端电脑** 上，下载并运行微软官方安装程序：

   👉 [DirectX 最终用户运行时（2010 年 6 月版）](https://www.microsoft.com/en-us/download/details.aspx?id=8109)

   > 该程序将安装所需的 DirectX 9 组件（`d3dx9_43.dll`）及悬浮层运行所需的其他依赖项。

2. 按照安装向导提示完成设置。

3. 重启雷达应用程序。完整重启电脑可能有助于解决问题，但通常无需操作。

**切勿**从非官方第三方 DLL 下载网站获取 `d3dx9_43.dll`，请仅使用微软官方安装程序。

## 贡献指南 🤝

欢迎通过提交 PR（拉取请求）参与项目贡献！

- 请先 Fork 本仓库，再针对功能开发或问题修复创建拉取请求。
- 提交 PR 前请自行测试修改内容。
- 若计划提交重大变更，建议先创建 Issue 进行讨论。
