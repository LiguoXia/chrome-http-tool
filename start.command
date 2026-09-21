#!/bin/bash
# ============================================
#  Chrome HTTP 演示工具 - macOS 启动脚本
#  双击运行；若提示无权限，先执行：chmod +x start.command
# ============================================
cd "$(dirname "$0")"

if ! command -v node >/dev/null 2>&1; then
  echo "[ERROR] 未找到 Node.js，请先安装：brew install node（或访问 nodejs.org）"
  read -r -p "按回车键退出…"
  exit 1
fi

echo "启动 Chrome HTTP 演示工具…"
echo "浏览器将自动打开 http://127.0.0.1:8787"
echo "关闭此窗口即停止服务。"
echo ""

open "http://127.0.0.1:8787"
node server.js

echo ""
echo "服务已停止。"
read -r -p "按回车键退出…"
