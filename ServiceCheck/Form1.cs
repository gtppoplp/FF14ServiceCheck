using System.Diagnostics;
using System.Media;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using ServiceCheck.Configuration;
using ServiceCheck.Models;
using Timer = System.Windows.Forms.Timer;

namespace ServiceCheck;

/// <summary>
/// 主窗体类，负责显示服务器监控界面和处理用户交互
/// </summary>
public partial class Form1 : Form {
    /// <summary>
    /// 当前是否正在监控服务器状态
    /// </summary>
    private bool isMonitoring;

    /// <summary>
    /// 定时器，用于定时检查服务器状态
    /// </summary>
    private Timer? monitorTimer;

    /// <summary>
    /// 托盘图标，用于在系统托盘显示监控状态
    /// </summary>
    private SoundPlayer? alertSound;

    /// <summary>
    /// 服务器区域列表，包含所有服务器的分组信息
    /// </summary>
    private readonly List<ServerArea> serverAreas = [];

    /// <summary>
    /// 最后一次检查时间标签
    /// </summary>
    private string? soundFileName;

    /// <summary>
    /// 提示音文件名
    /// </summary>
    private bool closeAppOnFormClosing;

    /// <summary>
    /// 服务器配置对象，包含所有服务器的配置信息
    /// </summary>
    private ServerConfiguration? configuration;

    /// <summary>
    /// 构造函数，初始化窗体和相关组件
    /// </summary>
    public Form1() {
        InitializeComponent();
        InitializeCustomComponents();
        _ = LoadServersAsync();
    }

    /// <summary>
    /// 初始化自定义组件和事件处理
    /// </summary>
    private void InitializeCustomComponents() {
        // 初始化提示音
        InitializeAlertSound();

        // 初始化定时器，10秒检查一次
        monitorTimer = new Timer();
        monitorTimer.Interval = 10000; // 10秒
        monitorTimer.Tick += MonitorTimer_Tick!;

        // 设置窗体大小和标题
        Text = @"FF14服务器监控工具";
        Size = new Size(800, 600);

        // 添加监控状态标签
        statusLabel.Text = @"监控状态: 未开启";

        // 为开关按钮添加事件处理
        toggleButton.Click -= ToggleButton_Click!;
        toggleButton.Click += ToggleButton_Click!;

        // 为测试提示音按钮添加事件处理
        testSoundButton.Click -= TestSoundButton_Click!;
        testSoundButton.Click += TestSoundButton_Click!;

        // 初始化托盘图标和菜单
        InitializeTrayIcon();
    }

    /// <summary>
    /// 初始化托盘图标和菜单项
    /// </summary>
    private void InitializeTrayIcon() {
        // 注意：NotifyIcon和菜单项已经由设计师创建
        // 我们只需要设置其他属性和事件
        notifyIcon.Icon = SystemIcons.Application;
        notifyIcon.Visible = true;
        notifyIcon.MouseDoubleClick += notifyIcon_MouseDoubleClick!;

        // 上下文菜单和项目已经在设计师中创建
        // 只需更新活动处理程序
        showMenuItem.Click += showMenuItem_Click!;
        toggleMonitorMenuItem.Click += toggleMonitorMenuItem_Click!;
        exitMenuItem.Click += exitMenuItem_Click!;

        UpdateTrayIconStatus();
    }

    /// <summary>
    /// 初始化提示音，尝试加载系统默认的提示音文件
    /// </summary>
    private void InitializeAlertSound() {
        try {
            string[] possibleSoundPaths = [
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Media",
                    "Windows Notify.wav"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Media", "Alarm01.wav"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Media",
                    "Windows Exclamation.wav"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Media", "notify.wav"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Media", "tada.wav")
            ];

            string soundPath = null!;
            foreach (var path in possibleSoundPaths) {
                if (!File.Exists(path)) continue;
                soundPath = path;
                soundFileName = Path.GetFileName(path);
                break;
            }

            alertSound = new SoundPlayer(soundPath);
            alertSound.LoadAsync();
            logListBox.Items.Insert(0, $"[{DateTime.Now}] 成功加载系统提示音: {soundFileName}");
        } catch (Exception ex) {
            MessageBox.Show($@"初始化提示音失败: {ex.Message}", @"错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            logListBox.Items.Insert(0, $"[{DateTime.Now}] 错误: 初始化提示音失败 - {ex.Message}");
        }
    }

    /// <summary>
    /// 测试提示音按钮点击事件处理
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private void TestSoundButton_Click(object sender, EventArgs e) {
        try {
            if (alertSound != null) {
                alertSound.Play();
                logListBox.Items.Insert(0, $"[{DateTime.Now}] 测试提示音播放: {soundFileName}");
            } else {
                MessageBox.Show(@"提示音未正确初始化，无法播放测试音频。", @"错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                logListBox.Items.Insert(0, $"[{DateTime.Now}] 错误: 提示音未正确初始化，无法测试");
            }
        } catch (Exception ex) {
            MessageBox.Show($@"播放提示音失败: {ex.Message}", @"错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            logListBox.Items.Insert(0, $"[{DateTime.Now}] 错误: 播放提示音失败 - {ex.Message}");
        }
    }

    /// <summary>
    /// 加载服务器配置和状态
    /// </summary>
    /// <exception cref="FileNotFoundException"></exception>
    /// <exception cref="InvalidOperationException"></exception>
    private async Task LoadServersAsync() {
        try {
            // 禁用控件
            serverTreeView.Enabled = false;
            toggleButton.Enabled = false;
            statusLabel.Text = @"状态: 正在加载服务器列表...";

            logListBox.Items.Insert(0, $"[{DateTime.Now}] 正在加载服务器配置...");

            // 从配置文件加载服务器信息
            var configPath = Path.Combine(Application.StartupPath, "ServerConfig.json");
            if (!File.Exists(configPath)) {
                throw new FileNotFoundException("配置文件 ServerConfig.json 不存在");
            }

            var configJson = await File.ReadAllTextAsync(configPath);
            configuration = JsonSerializer.Deserialize<ServerConfiguration>(configJson, new JsonSerializerOptions {
                PropertyNameCaseInsensitive = true
            });

            if (configuration?.ServerAreas == null) {
                throw new InvalidOperationException("配置文件格式无效");
            }

            // 清空现有数据
            serverAreas.Clear();
            serverTreeView.Nodes.Clear();

            // 复制配置中的服务器数据
            foreach (var configArea in configuration.ServerAreas) {
                var serverArea = new ServerArea(configArea.Name, []);

                var areaNode = new TreeNode(configArea.Name) {
                    Tag = serverArea,
                    Checked = true
                };

                foreach (var configServer in configArea.Servers) {
                    var server = new Server(
                        configServer.Name,
                        configServer.IpAddress,
                        configServer.Port,
                        // 初始状态为未知
                        false,
                        // 默认选择所有服务器
                        true,
                        0);

                    serverArea.Servers.Add(server);

                    var serverNode = new TreeNode(configServer.Name) {
                        Checked = true,
                        Tag = server
                    };
                    UpdateServerNodeText(serverNode, server);

                    areaNode.Nodes.Add(serverNode);
                }

                serverAreas.Add(serverArea);
                serverTreeView.Nodes.Add(areaNode);
                areaNode.Expand();
            }

            logListBox.Items.Insert(0, $"[{DateTime.Now}] 成功加载服务器配置，共 {serverAreas.Count} 个区域");

            // 立即检查一次服务器状态
            await CheckAllServersAsync();
        } catch (Exception ex) {
            logListBox.Items.Insert(0, $"[{DateTime.Now}] 加载服务器配置失败: {ex.Message}");
            MessageBox.Show($@"加载服务器配置失败: {ex.Message}", @"错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
        } finally {
            // 启用控件
            serverTreeView.Enabled = true;
            toggleButton.Enabled = true;
            statusLabel.Text = @"监控状态: 未开启";
        }
    }

    /// <summary>
    /// 更新服务器节点的文本和颜色
    /// </summary>
    /// <param name="node"></param>
    /// <param name="server"></param>
    private static void UpdateServerNodeText(TreeNode node, Server server) {
        var status = server.IsRunning ? "在线" : "离线";
        var pingText = server.LastPingTime > 0 ? $" ({server.LastPingTime:F1}ms)" : "";
        node.Text = $@"{server.Name} [{status}]{pingText}";
        node.ForeColor = server.IsRunning ? Color.Green : Color.Red;
    }

    /// <summary>
    /// 检查服务器的连接状态
    /// </summary>
    /// <param name="server"></param>
    /// <returns></returns>
    private async Task<bool> CheckServerConnectivityAsync(Server server) {
        try {
            var stopwatch = Stopwatch.StartNew();
            using var client = new TcpClient();

            // 设置连接超时
            var connectTask = client.ConnectAsync(IPAddress.Parse(server.IpAddress), server.Port);
            var timeoutTask = Task.Delay(AppSettings.ConnectionTimeoutMs);

            var completedTask = await Task.WhenAny(connectTask, timeoutTask);
            stopwatch.Stop();

            if (completedTask == connectTask && client.Connected) {
                server.LastPingTime = stopwatch.Elapsed.TotalMilliseconds;
                client.Close();
                return true;
            }

            server.LastPingTime = 0;
            return false;
        } catch (Exception ex) {
            logListBox.Items.Insert(0, $"[{DateTime.Now}] 连接 {server.Name} 失败: {ex.Message}");
            server.LastPingTime = 0;
            return false;
        }
    }

    /// <summary>
    /// 检查所有服务器的状态
    /// </summary>
    private async Task CheckAllServersAsync() {
        logListBox.Items.Insert(0, $"[{DateTime.Now}] 开始检查所有服务器状态...");

        var tasks = serverAreas.SelectMany(area => area.Servers)
            .Select(async server => {
                var isOnline = await CheckServerConnectivityAsync(server);
                server.IsRunning = isOnline;
                return new { Server = server, IsOnline = isOnline };
            });

        var results = await Task.WhenAll(tasks);

        // 更新UI
        foreach (TreeNode areaNode in serverTreeView.Nodes) {
            foreach (TreeNode serverNode in areaNode.Nodes) {
                if (serverNode.Tag is Server server) {
                    UpdateServerNodeText(serverNode, server);
                }
            }
        }

        var onlineCount = results.Count(r => r.IsOnline);
        var totalCount = results.Length;
        logListBox.Items.Insert(0, $"[{DateTime.Now}] 检查完成: {onlineCount}/{totalCount} 服务器在线");
    }

    /// <summary>
    /// 定时器每次触发时检查选中的服务器状态
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private async void MonitorTimer_Tick(object sender, EventArgs e) {
        try {
            var newlyOnlineServers = new List<string>();

            // 只检查选中的服务器
            var selectedServers = serverAreas.SelectMany(area => area.Servers)
                .Where(s => s.IsSelected)
                .ToList();

            logListBox.Items.Insert(0, $"[{DateTime.Now}] 开始同时检查 {selectedServers.Count} 个服务器状态...");

            // 记录每个服务器检查前的状态
            var serverStates = selectedServers.ToDictionary(s => s, s => s.IsRunning);

            // 并发检查所有选中的服务器
            var checkTasks = selectedServers.Select(async server => {
                var isOnline = await CheckServerConnectivityAsync(server);
                server.IsRunning = isOnline;
                return new { Server = server, IsOnline = isOnline, WasOnline = serverStates[server] };
            });

            var results = await Task.WhenAll(checkTasks);

            // 更新UI和处理状态变化
            foreach (var result in results) {
                // 更新UI中对应的节点
                foreach (TreeNode areaNode in serverTreeView.Nodes) {
                    foreach (TreeNode serverNode in areaNode.Nodes) {
                        if (serverNode.Tag != result.Server) continue;
                        UpdateServerNodeText(serverNode, result.Server);
                        break;
                    }
                }

                // 如果服务器从离线变为在线，则记录并加入通知列表
                if (result.WasOnline || !result.IsOnline) continue;
                var areaName = serverAreas.First(a => a.Servers.Contains(result.Server)).Name;
                var message = $"{areaName} - {result.Server.Name} 服务器上线! (延时: {result.Server.LastPingTime:F1}ms)";
                logListBox.Items.Insert(0, $"[{DateTime.Now}] {message}");
                newlyOnlineServers.Add(message);
            }

            // 如果有新上线的服务器，则播放提示音并显示通知
            if (newlyOnlineServers.Count > 0) {
                try {
                    if (AppSettings.BeepEnabled) {
                        alertSound?.Play();
                    }

                    // 显示气泡通知
                    const string notificationTitle = "服务器可用提醒";
                    var notificationText = string.Join("\n", newlyOnlineServers);

                    notifyIcon.BalloonTipTitle = notificationTitle;
                    notifyIcon.BalloonTipText = notificationText;
                    notifyIcon.BalloonTipIcon = ToolTipIcon.Info;
                    notifyIcon.ShowBalloonTip(30000);
                } catch (Exception ex) {
                    logListBox.Items.Insert(0, $"[{DateTime.Now}] 播放提示音失败: {ex.Message}");
                }
            }

            var onlineCount = results.Count(r => r.IsOnline);
            logListBox.Items.Insert(0, $"[{DateTime.Now}] 同时检查完成: {onlineCount}/{selectedServers.Count} 服务器在线");
            
            // 更新最后检查时间
            lastCheckLabel.Text = $@"最后检查: {DateTime.Now:yyyy-MM-dd HH:mm:ss}";
        } catch (Exception ex) {
            logListBox.Items.Insert(0, $"[{DateTime.Now}] 监控错误: {ex.Message}");
        }
    }

    /// <summary>
    /// 切换监控状态按钮点击事件处理
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private void ToggleButton_Click(object sender, EventArgs e) {
        ToggleMonitoring();
    }

    /// <summary>
    /// 切换监控状态
    /// </summary>
    private void ToggleMonitoring() {
        isMonitoring = !isMonitoring;

        if (isMonitoring) {
            monitorTimer?.Start();
            toggleButton.Text = @"停止监控";
            toggleMonitorMenuItem.Text = @"停止监控";
            statusLabel.Text = @"监控状态: 已开启";
            logListBox.Items.Insert(0, $"[{DateTime.Now}] 开始监控服务器状态 (每10秒检查一次)");
        } else {
            monitorTimer?.Stop();
            toggleButton.Text = @"开始监控";
            toggleMonitorMenuItem.Text = @"开始监控";
            statusLabel.Text = @"监控状态: 未开启";
            logListBox.Items.Insert(0, $"[{DateTime.Now}] 停止监控服务器状态");
        }

        UpdateTrayIconStatus();
    }

    /// <summary>
    /// 更新托盘图标的状态文本
    /// </summary>
    private void UpdateTrayIconStatus() {
        notifyIcon.Text = $@"FF14服务器监控工具 - {(isMonitoring ? "监控中" : "未监控")}";
    }

    /// <summary>
    /// 处理服务器树视图的节点检查状态变化事件
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private void serverTreeView_AfterCheck(object sender, TreeViewEventArgs e) {
        if (e.Action is not (TreeViewAction.ByMouse or TreeViewAction.ByKeyboard)) return;
        switch (e.Node?.Tag) {
            case Server server:
                server.IsSelected = e.Node.Checked;
                break;
            case ServerArea: {
                foreach (TreeNode serverNode in e.Node.Nodes) {
                    serverNode.Checked = e.Node.Checked;
                    if (serverNode.Tag is Server childServer) {
                        childServer.IsSelected = e.Node.Checked;
                    }
                }

                break;
            }
        }
    }

    /// <summary>
    /// 刷新按钮点击事件处理
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private void refreshButton_Click(object sender, EventArgs e) {
        _ = LoadServersAsync();
    }

    /// <summary>
    /// 窗体大小改变事件 - 最小化到托盘
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private void Form1_Resize(object sender, EventArgs e) {
        if (WindowState != FormWindowState.Minimized) return;
        Hide();
        notifyIcon.BalloonTipTitle = @"FF14服务器监控工具";
        notifyIcon.BalloonTipText = @"程序已最小化到系统托盘，将在后台继续监控服务器状态。";
        notifyIcon.BalloonTipIcon = ToolTipIcon.Info;
        notifyIcon.ShowBalloonTip(3000);
    }

    /// <summary>
    /// 窗体关闭事件
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private void Form1_FormClosing(object sender, FormClosingEventArgs e) {
        if (closeAppOnFormClosing) return;
        e.Cancel = true;
        Hide();
        notifyIcon.BalloonTipTitle = @"FF14服务器监控工具";
        notifyIcon.BalloonTipText = """
                                    程序已最小化到系统托盘，将在后台继续监控服务器状态。
                                    如需完全退出，请右键托盘图标选择"退出"。
                                    """;
        notifyIcon.BalloonTipIcon = ToolTipIcon.Info;
        notifyIcon.ShowBalloonTip(5000);
    }

    /// <summary>
    /// 托盘图标双击事件
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private void notifyIcon_MouseDoubleClick(object sender, MouseEventArgs e) {
        if (e.Button != MouseButtons.Left) return;
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
    }

    /// <summary>
    /// 托盘菜单 - 显示主窗口
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private void showMenuItem_Click(object sender, EventArgs e) {
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
    }

    /// <summary>
    /// 托盘菜单 - 切换监控状态
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private void toggleMonitorMenuItem_Click(object sender, EventArgs e) {
        ToggleMonitoring();
    }

    /// <summary>
    /// 托盘菜单 - 退出应用程序
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="e"></param>
    private void exitMenuItem_Click(object sender, EventArgs e) {
        if (isMonitoring) {
            var result = MessageBox.Show(@"监控正在进行中，确定要退出应用吗？", @"确认退出",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question);

            if (result != DialogResult.Yes) {
                return;
            }
        }

        closeAppOnFormClosing = true;

        if (monitorTimer != null) {
            monitorTimer.Stop();
            monitorTimer.Dispose();
        }

        alertSound?.Dispose();

        Application.Exit();
    }
}