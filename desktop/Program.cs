using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Web.WebView2.WinForms;

namespace HttpTool;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        string baseDir = AppContext.BaseDirectory;
        int port = FindFreePort(8787);

        AppServer server = new AppServer(port, baseDir);
        try
        {
            server.Start();
        }
        catch (Exception ex)
        {
            MessageBox.Show("本地服务启动失败：" + ex.Message, "HTTP 演示工具",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        // 无窗口模式（自动化测试 / 排障）
        if (Array.Exists(args, a => a == "--server-only"))
        {
            Thread.Sleep(Timeout.Infinite);
            server.Stop();
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm(port));
        server.Stop();
    }

    private static int FindFreePort(int start)
    {
        for (int p = start; p < start + 100; p++)
        {
            try
            {
                var l = new TcpListener(IPAddress.Loopback, p);
                l.Start();
                l.Stop();
                return p;
            }
            catch (SocketException) { }
        }
        throw new InvalidOperationException("找不到可用端口");
    }
}

internal sealed class MainForm : Form
{
    private readonly WebView2 _web;

    public MainForm(int port)
    {
        Text = "HTTP 演示工具";
        Width = 1400;
        Height = 900;
        MinimumSize = new System.Drawing.Size(1000, 700);
        StartPosition = FormStartPosition.CenterScreen;
        try
        {
            Icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        }
        catch { /* 图标提取失败不影响使用 */ }

        _web = new WebView2 { Dock = DockStyle.Fill };
        Controls.Add(_web);

        Load += async (_, _) =>
        {
            try
            {
                string userData = Path.Combine(AppContext.BaseDirectory, ".webview2");
                var env = await Microsoft.Web.WebView2.Core.CoreWebView2Environment
                    .CreateAsync(null, userData);
                await _web.EnsureCoreWebView2Async(env);
                _web.CoreWebView2.Settings.AreDevToolsEnabled = false;
                _web.CoreWebView2.Settings.IsStatusBarEnabled = false;
                _web.CoreWebView2.Settings.IsZoomControlEnabled = true;
                _web.Source = new Uri("http://127.0.0.1:" + port + "/");
            }
            catch (Exception ex)
            {
                MessageBox.Show("WebView2 初始化失败（需要 Edge WebView2 运行时）：\n" + ex.Message,
                    "HTTP 演示工具", MessageBoxButtons.OK, MessageBoxIcon.Error);
                Application.Exit();
            }
        };
        FormClosing += (_, _) => Application.Exit();
    }
}
