"use strict";

/**
 * HTTP 演示工具 — macOS 桌面版主进程（Electron）
 *
 * 架构：复用根目录 server.js（零依赖 Node 本地服务）+ public/index.html 前端。
 * 主进程 fork server.js 子进程 → 等待端口就绪 → 打开 BrowserWindow 加载页面。
 * 用户数据保存在 macOS 用户目录（.app 只读，不能写包内），通过 HTTP_TOOL_DATA_DIR 传给 server.js。
 */

const { app, BrowserWindow, Menu, shell } = require("electron");
const { fork } = require("child_process");
const path = require("path");
const fs = require("fs");
const net = require("net");

let serverProc = null;
let mainWindow = null;

/** 定位 server.js 所在目录（开发模式 vs 打包模式） */
function serverDir() {
  if (app.isPackaged) {
    // 打包后：extraResources 被放到 Resources/server/
    return path.join(process.resourcesPath, "server");
  }
  // 开发模式：macos/ 的上级（项目根）
  return path.join(__dirname, "..");
}

/** 从指定端口开始寻找可用端口（同 Windows 版 FindFreePort 语义） */
function findFreePort(start) {
  return new Promise((resolve, reject) => {
    const tryPort = (p) => {
      if (p >= start + 100) return reject(new Error("找不到可用端口"));
      const srv = net.createServer();
      srv.once("error", () => tryPort(p + 1));
      srv.once("listening", () => {
        const port = srv.address().port;
        srv.close(() => resolve(port));
      });
      srv.listen(p, "127.0.0.1");
    };
    tryPort(start);
  });
}

/** 启动 server.js 子进程并等待其端口就绪 */
function startServer(port) {
  const srvDir = serverDir();
  const serverJs = path.join(srvDir, "server.js");
  if (!fs.existsSync(serverJs)) {
    return Promise.reject(new Error("未找到 server.js：" + serverJs));
  }
  const dataDir = path.join(app.getPath("userData"), "data");
  fs.mkdirSync(dataDir, { recursive: true });

  serverProc = fork(serverJs, [String(port)], {
    env: {
      ...process.env,
      PORT: String(port),
      HTTP_TOOL_DATA_DIR: dataDir,
    },
    stdio: "ignore",
  });

  return new Promise((resolve, reject) => {
    const deadline = Date.now() + 15000;
    const poll = () => {
      if (serverProc && serverProc.exitCode != null) {
        return reject(new Error("本地服务启动失败（server.js 异常退出）"));
      }
      const sock = net.connect(port, "127.0.0.1");
      sock.once("connect", () => { sock.destroy(); resolve(); });
      sock.once("error", () => {
        sock.destroy();
        if (Date.now() > deadline) return reject(new Error("本地服务启动超时"));
        setTimeout(poll, 120);
      });
    };
    poll();
  });
}

/** 精简应用菜单：保留系统快捷键（Cmd+C/V/Q），不暴露 Reload / DevTools */
function setupMenu() {
  const isMac = process.platform === "darwin";
  const template = [
    ...(isMac ? [{
      label: app.name,
      submenu: [
        { role: "about" },
        { type: "separator" },
        { role: "services" },
        { type: "separator" },
        { role: "hide" },
        { role: "hideOthers" },
        { role: "unhide" },
        { type: "separator" },
        { role: "quit" },
      ],
    }] : []),
    {
      label: "编辑",
      submenu: [
        { role: "undo" },
        { role: "redo" },
        { type: "separator" },
        { role: "cut" },
        { role: "copy" },
        { role: "paste" },
        { role: "selectAll" },
      ],
    },
    {
      label: "显示",
      submenu: [
        { role: "zoomIn" },
        { role: "zoomOut" },
        { role: "resetZoom" },
        { type: "separator" },
        { role: "togglefullscreen" },
      ],
    },
    {
      label: "窗口",
      submenu: [
        { role: "minimize" },
        ...(isMac ? [{ role: "zoom" }, { type: "separator" }, { role: "front" }] : [{ role: "close" }]),
      ],
    },
  ];
  Menu.setApplicationMenu(Menu.buildFromTemplate(template));
}

function createWindow(port) {
  mainWindow = new BrowserWindow({
    width: 1400,
    height: 900,
    minWidth: 1000,
    minHeight: 700,
    title: "HTTP 演示工具",
    backgroundColor: "#f5f5f7",
    show: false,
    webPreferences: {
      contextIsolation: true,
      nodeIntegration: false,
    },
  });

  mainWindow.once("ready-to-show", () => mainWindow.show());
  mainWindow.loadURL("http://127.0.0.1:" + port + "/");

  // 页面内的外链一律用系统浏览器打开，不在应用内跳转
  mainWindow.webContents.setWindowOpenHandler(({ url }) => {
    if (/^https?:\/\//.test(url)) shell.openExternal(url);
    return { action: "deny" };
  });
  mainWindow.webContents.on("will-navigate", (e, url) => {
    if (!url.startsWith("http://127.0.0.1:" + port)) {
      e.preventDefault();
      if (/^https?:\/\//.test(url)) shell.openExternal(url);
    }
  });

  mainWindow.on("closed", () => { mainWindow = null; });
}

function stopServer() {
  if (serverProc) {
    try { serverProc.kill(); } catch (e) { /* 忽略 */ }
    serverProc = null;
  }
}

app.whenReady().then(async () => {
  setupMenu();
  try {
    const port = await findFreePort(8787);
    await startServer(port);
    createWindow(port);

    app.on("activate", () => {
      if (BrowserWindow.getAllWindows().length === 0) createWindow(port);
    });
  } catch (err) {
    const { dialog } = require("electron");
    dialog.showErrorBox("HTTP 演示工具", "启动失败：" + err.message);
    app.quit();
  }
});

app.on("window-all-closed", () => {
  // 单窗口工具：窗口全部关闭即退出（macOS 上也退出）
  app.quit();
});

app.on("quit", () => {
  stopServer();
});
