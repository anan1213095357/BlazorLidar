# 📡 战术雷达终端 (Tactical Radar Terminal)

![.NET](https://img.shields.io/badge/.NET-8.0-512BD4)
![Blazor](https://img.shields.io/badge/Blazor-Server-purple)
![License](https://img.shields.io/badge/license-MIT-green)
![Status](https://img.shields.io/badge/Status-Experimental-orange)

**Tactical Radar Terminal** 是一个基于 **Blazor Server** 构建的高性能激光雷达（LiDAR）可视化监控系统。

它摒弃了传统的工业界面，采用 **CRT 复古未来主义 (Cyberpunk/Sci-Fi)** 的战术风格 UI，支持实时点云渲染、区域入侵检测报警以及硬件直连控制。

---

## ✨ 核心特性

- **🖥️ 沉浸式战术 UI**: 
  - 纯 CSS 实现的 CRT 扫描线与荧光屏效果。
  - 动态呼吸灯、状态指示器与暗黑/荧光绿军用配色。
- **⚡ 高性能渲染**:
  - 利用 Blazor Server 与 HTML5 Canvas (JS Interop) 协作，实现流畅的点云绘制。
  - 优化的数据缓冲区处理，支持高频雷达数据吞吐。
- **🛡️ 交互式防御布防**:
  - **拖拽布防**: 在雷达扫描图上直接拖拽绘制圆形警戒区 (Zone)。
  - **入侵检测**: 实时计算点云坐标，一旦有物体进入警戒区立即触发逻辑。
- **🚨 报警日志系统**:
  - 自动记录入侵事件的时间戳。
  - UI 视觉闪烁警报反馈。
- **⚙️ 硬件控制**:
  - 支持配置串口号 (COM Port) 和波特率 (Baud Rate)。
  - 提供一键启动扫描与紧急切断电源功能。

---

## 📸 界面预览

> *在此处上传你的运行截图，建议放置一张 GIF 展示扫描效果*

| 待机模式 | 扫描与布防 |
| :---: | :---: |
| ![Standby](<img width="2095" height="1390" alt="22293163-1a1d-47b7-8489-9df591efb0fc" src="https://github.com/user-attachments/assets/9d70ebf2-c48c-45e6-bc60-8ab66ae32b81" />
) | ![Scanning](https://via.placeholder.com/400x300?text=Scanning+Active) |

---

## 🛠️ 技术栈

- **框架**: .NET 8.0 / Blazor Server
- **前端**: HTML5, CSS3 (Variables, Animations), JavaScript (Canvas API)
- **硬件通信**: System.IO.Ports
- **数据处理**: C# 后端处理雷达协议解析与坐标转换

---

## 🚀 快速开始

### 1. 环境准备
- 安装 [.NET SDK](https://dotnet.microsoft.com/download) (推荐 .NET 6.0 或更高)。
- 准备一个支持串口通信的激光雷达（如 RPLIDAR A1/A2, YDLIDAR 等）或使用模拟数据源。

### 2. 克隆项目
```bash
git clone [https://github.com/your-username/blazor-tactical-lidar.git](https://github.com/your-username/blazor-tactical-lidar.git)
cd blazor-tactical-lidar
