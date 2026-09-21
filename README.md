# HTTP 演示工具（chrome-http-tool）

一个面向**接口演示 / 调试**场景的本地 HTTP 客户端工具：配置接口后一键发送，实时展示请求与响应，支持多环境切换、全局变量、状态轮询与 curl 命令生成。界面遵循苹果（Apple HIG）设计风格。

## ✨ 功能特性

- **场景管理**：场景间配置完全隔离；支持分组、拖拽排序、复制、重命名、导入 / 导出
- **多环境配置**：每个环境独立持有全局请求头、全局变量与请求选项；支持一键从其他环境复制配置
- **接口操作**：GET / POST / PUT / DELETE / PATCH，JSON / FORM / QUERY / RAW 四种参数类型
- **JSON 编辑器**：参数内容实时语法高亮、一键格式化；日志与轮询返回同样自动高亮
- **全局变量**：`{{变量名}}` 引用，自动替换；变量随环境切换取值
- **代理转发**：本地服务端转发请求，绕过浏览器 CORS 限制；支持超时、忽略证书校验
- **curl 生成**：一键复制为 cmd / bash 格式 curl 命令；支持粘贴 curl 反向导入接口
- **状态轮询**：独立轮询通道（不干扰主操作区），可收起、可拖拽调宽
- **请求日志时间线**：历史请求按时间线展示（成功绿点 / 失败红点），点击展开完整请求与响应（JSON 高亮），最新一条自动展开
- **日志持久化**：完整请求历史自动落盘，每天一个日志文件、每月一个文件夹（见下文「数据存储」）
- **布局个性化**：三栏面板拖拽调宽、轮询面板收起展开、侧栏调宽，全部自动记忆
- **数据本地化**：所有数据保存在工具目录 `data` 下，不使用浏览器存储，无云端依赖

## 🖥️ 运行环境

| 版本 | 要求 |
| --- | --- |
| Windows 客户端（推荐） | Windows 10 / 11 + Edge WebView2 运行时（Win10/11 通常已内置） |
| 浏览器版（Windows） | Node.js ≥ 16 |
| 浏览器版（macOS） | Node.js ≥ 16 |

## 🚀 快速开始

### 方式一：Windows 客户端（零依赖）

1. 到 [Releases](../../releases) 下载 `HttpTool-Client-xxx.zip` 并解压
2. 双击 `HttpTool-Client.exe` 即可使用
3. 数据自动保存在 exe 同级的 `data` 目录，拷贝 exe 到任何 Windows 电脑都能直接运行

### 方式二：浏览器版（源码运行）

```bash
# Windows：双击 start.bat
# macOS：首次执行 chmod +x start.command，之后双击 start.command
# 或手动启动：
node server.js
# 然后浏览器访问 http://127.0.0.1:8787
```

> **局域网访问**：服务默认监听所有网卡，同一局域网内的其他设备（手机、平板、同事电脑）可通过
> `http://<本机局域网IP>:8787` 访问（启动时终端会打印完整地址）。
> 若无法访问，请在 Windows 防火墙中放行 Node.js（首次监听时弹出的防火墙提示点「允许访问」即可）。
> 注意：局域网访问没有鉴权，仅在可信网络中使用。

## 🔨 构建

### 浏览器版（无需构建）

`public/index.html` + `server.js` 即全部源码，直接 `node server.js` 运行。

### Windows 客户端

要求：.NET 8 SDK（[下载](https://dotnet.microsoft.com/download/dotnet/8.0)）

```bash
cd desktop
dotnet publish -c Release -o dist
# 产物：dist/HttpTool.exe（自包含单文件，约 75MB）
```

发布说明：

- `HttpTool.csproj`：`PublishSingleFile` + `SelfContained`，无需目标机器安装 .NET
- 页面 `public/index.html` 会同时嵌入 exe（单独拷贝 exe 也能运行），也保留外部文件以便调试
- 应用图标：`desktop/app-icon.png`（源图）→ `app.ico`（9 尺寸），重建入口 `desktop/make-icon.py`（图形素材来自 Carbon 图标集，Apache-2.0 许可）

### 数据格式

- 索引文件：`data/index.json`（场景元信息、分组、UI 布局偏好）
- 场景数据：`data/scenes/{sceneId}.json`（每场景独立文件，可直接编辑或迁移）
- 请求日志：`data/logs/{场景ID}/{环境ID}/年-月/年-月-日.jsonl`（JSON Lines，每行一条请求记录；按场景、环境分别记录，每天一个文件、每月一个文件夹，方便归档与检索）

## 📖 使用说明

1. **新建场景**：左侧「+」创建场景（不同演示主题建不同场景，配置互不影响）
2. **配置环境**：右上角切换环境；点击「全局设置」可管理每个环境的请求头、全局变量、请求选项，支持从其他环境一键复制配置再微调
3. **配置接口**：点击「新增接口」或「从 curl 导入」；接口卡片支持发送、编辑、复制接口、复制 curl、拖拽排序
4. **全局变量**：定义后接口 URL、请求头、参数中均可 `{{变量名}}` 引用，发送前自动替换
5. **发送与查看**：点「发送」立即请求；「请求 / 响应日志」面板展示最近一次请求 / 响应的完整内容（JSON 自动格式化高亮）
6. **状态轮询**：右面板独立轮询接口，间隔可调，适合演示时持续观察状态；面板可收起（贴边）或拖拽调整宽度
7. **导入导出**：侧栏「导出」备份场景 JSON；「导入」恢复；数据目录随项目迁移即可

## 📁 项目结构

```
chrome-http-tool/
├── server.js              # 浏览器版本地服务（Node）
├── start.bat              # Windows 双击启动
├── start.command          # macOS 双击启动
├── public/
│   └── index.html         # 全部前端（界面 + 逻辑，单文件）
├── desktop/               # Windows 客户端（.NET 8 + WebView2）
│   ├── HttpTool.csproj
│   ├── Program.cs         # 窗口壳 + WebView2
│   ├── Server.cs          # 内嵌 HTTP 服务（与 server.js API 一致）
│   ├── app-icon.png       # 应用图标（1024）
│   ├── app.ico            # 多尺寸图标
│   └── make-icon.py       # 图标重建脚本
└── data/                  # 用户数据（运行时自动创建，不入库）
    ├── index.json
    └── scenes/
```

## ⚠️ 注意事项

- 客户端与浏览器版共用数据格式，可交替使用，但**不要同时打开**（同端口同数据目录会冲突）
- 端口默认 8787，被占用时客户端会自动顺延
- 代理模式仅支持 http/https 绝对地址

## 🔒 隐私

- 所有数据仅保存在本地 `data` 目录
- 工具不收集、不上传任何数据
