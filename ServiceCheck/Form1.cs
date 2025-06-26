using System.Media;
using System.Text.Json;
using ServiceCheck.Models;
using Timer = System.Windows.Forms.Timer;

namespace ServiceCheck {
    public partial class Form1 : Form {
        private bool isMonitoring = false;
        private Timer monitorTimer; // 明确使用 System.Windows.Forms.Timer
        private HttpClient httpClient;
        private SoundPlayer alertSound;
        private List<ServerArea> serverAreas = new List<ServerArea>();
        private string soundFileName; // 保存当前使用的系统声音文件名
        private bool closeAppOnFormClosing = false; // 控制关闭行为

        private NotifyIcon notifyIcon;
        private ContextMenuStrip trayMenu;
        private ToolStripMenuItem showMenuItem;
        private ToolStripMenuItem toggleMonitorMenuItem;
        private ToolStripMenuItem exitMenuItem;

        public Form1() {
            InitializeComponent();

            // 初始化组件
            InitializeCustomComponents();

            // 应用启动时获取一次服务器状态
            _ = LoadServersAsync();
        }

        private void InitializeCustomComponents() {
            // 初始化HTTP客户端
            httpClient = new HttpClient();

            // 初始化提示音
            InitializeAlertSound();

            // 初始化定时器，10秒检查一次（修改为10秒）
            monitorTimer = new Timer();
            monitorTimer.Interval = 10000; // 10秒
            monitorTimer.Tick += MonitorTimer_Tick;

            // 设置窗体大小和标题
            this.Text = "服务监控工具";
            this.Size = new Size(800, 600);

            // 添加监控状态标签
            statusLabel.Text = "监控状态: 未开启";

            // 为开关按钮添加事件处理
            toggleButton.Click -= ToggleButton_Click;
            toggleButton.Click += ToggleButton_Click;

            // 为测试提示音按钮添加事件处理
            testSoundButton.Click -= TestSoundButton_Click;
            testSoundButton.Click += TestSoundButton_Click;

            // 初始化托盘图标和菜单
            InitializeTrayIcon();
        }

        private void InitializeTrayIcon() {
            notifyIcon = new NotifyIcon();
            notifyIcon.Icon = SystemIcons.Application;
            notifyIcon.Visible = true;
            notifyIcon.MouseDoubleClick += notifyIcon_MouseDoubleClick;

            trayMenu = new ContextMenuStrip();
            showMenuItem = new ToolStripMenuItem("显示主窗口", null, showMenuItem_Click);
            toggleMonitorMenuItem = new ToolStripMenuItem("开始监控", null, toggleMonitorMenuItem_Click);
            exitMenuItem = new ToolStripMenuItem("退出", null, exitMenuItem_Click);

            trayMenu.Items.AddRange(new ToolStripItem[] { showMenuItem, toggleMonitorMenuItem, exitMenuItem });
            notifyIcon.ContextMenuStrip = trayMenu;

            UpdateTrayIconStatus();
        }

        private void InitializeAlertSound() {
            try {
                // 使用Windows系统自带的声音文件作为提示音
                // 常见的Windows系统声音路径
                string[] possibleSoundPaths = {
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Media",
                        "Windows Notify.wav"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Media", "Alarm01.wav"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Media",
                        "Windows Exclamation.wav"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Media", "notify.wav"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Media", "tada.wav")
                };

                // 查找第一个存在的声音文件
                string soundPath = null;
                foreach (var path in possibleSoundPaths) {
                    if (File.Exists(path)) {
                        soundPath = path;
                        soundFileName = Path.GetFileName(path);
                        break;
                    }
                }

                if (soundPath != null) {
                    alertSound = new SoundPlayer(soundPath);
                    // 立即加载音频到内存以加速后续播放
                    alertSound.LoadAsync();
                    logListBox.Items.Insert(0, $"[{DateTime.Now}] 成功加载系统提示音: {soundFileName}");
                } else {
                    MessageBox.Show("无法找到可用的系统提示音文件，监控时将无法播放提示音。", "警告", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    logListBox.Items.Insert(0, $"[{DateTime.Now}] 警告: 未找到可用的系统提示音文件");
                }
            } catch (Exception ex) {
                MessageBox.Show($"初始化提示音失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                logListBox.Items.Insert(0, $"[{DateTime.Now}] 错误: 初始化提示音失败 - {ex.Message}");
            }
        }

        private void TestSoundButton_Click(object sender, EventArgs e) {
            try {
                if (alertSound != null) {
                    alertSound.Play();
                    logListBox.Items.Insert(0, $"[{DateTime.Now}] 测试提示音播放: {soundFileName}");
                } else {
                    MessageBox.Show("提示音未正确初始化，无法播放测试音频。", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    logListBox.Items.Insert(0, $"[{DateTime.Now}] 错误: 提示音未正确初始化，无法测试");
                }
            } catch (Exception ex) {
                MessageBox.Show($"播放提示音失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                logListBox.Items.Insert(0, $"[{DateTime.Now}] 错误: 播放提示音失败 - {ex.Message}");
            }
        }

        private async Task LoadServersAsync() {
            try {
                // 禁用控件
                serverTreeView.Enabled = false;
                toggleButton.Enabled = false;
                statusLabel.Text = "状态: 正在加载服务器列表...";

                // 请求服务状态API
                logListBox.Items.Insert(0, $"[{DateTime.Now}] 正在请求服务器列表...");
                var response =
                    await httpClient.GetStringAsync("https://ff14act.web.sdo.com/api/serverStatus/getServerStatus");
                logListBox.Items.Insert(0, $"[{DateTime.Now}] 收到服务器列表响应");

                // 解析JSON响应
                using var jsonDoc = JsonDocument.Parse(response);
                var root = jsonDoc.RootElement;

                // 清空现有数据
                serverAreas.Clear();
                serverTreeView.Nodes.Clear();

                // 处理所有区域的所有服务器
                foreach (var area in root.GetProperty("Data").EnumerateArray()) {
                    var areaName = area.GetProperty("AreaName").GetString();
                    var serverArea = new ServerArea { Name = areaName, Servers = new List<Server>() };

                    var areaNode = new TreeNode(areaName);
                    areaNode.Tag = serverArea;
                    areaNode.Checked = true; // 确保区域节点也被选中

                    foreach (var group in area.GetProperty("Group").EnumerateArray()) {
                        var serverName = group.GetProperty("name").GetString();
                        var isRunning = group.GetProperty("runing").GetBoolean();

                        var server = new Server {
                            Name = serverName,
                            IsRunning = isRunning,
                            IsSelected = true // 默认选择所有服务器
                        };

                        serverArea.Servers.Add(server);

                        var serverNode = new TreeNode(serverName);
                        serverNode.Checked = true; // 确保默认勾选
                        serverNode.Tag = server;
                        UpdateServerNodeText(serverNode, server);

                        areaNode.Nodes.Add(serverNode);
                    }

                    serverAreas.Add(serverArea);
                    serverTreeView.Nodes.Add(areaNode);
                    areaNode.Expand(); // 展开区域节点
                }

                logListBox.Items.Insert(0, $"[{DateTime.Now}] 成功加载服务器列表，共 {serverAreas.Count} 个区域");
            } catch (Exception ex) {
                logListBox.Items.Insert(0, $"[{DateTime.Now}] 加载服务器列表失败: {ex.Message}");
                MessageBox.Show($"加载服务器列表失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            } finally {
                // 启用控件
                serverTreeView.Enabled = true;
                toggleButton.Enabled = true;
                statusLabel.Text = "监控状态: 未开启";
            }
        }

        private void UpdateServerNodeText(TreeNode node, Server server) {
            node.Text = $"{server.Name} [{(server.IsRunning ? "运行中" : "未运行")}]";
            node.ForeColor = server.IsRunning ? Color.Green : Color.Red;
        }

        private async void MonitorTimer_Tick(object sender, EventArgs e) {
            try {
                // 请求服务状态API
                logListBox.Items.Insert(0, $"[{DateTime.Now}] 正在检查服务器状态...");
                var response =
                    await httpClient.GetStringAsync("https://ff14act.web.sdo.com/api/serverStatus/getServerStatus");
                logListBox.Items.Insert(0, $"[{DateTime.Now}] 收到服务器状态响应");

                // 解析JSON响应
                using var jsonDoc = JsonDocument.Parse(response);
                var root = jsonDoc.RootElement;

                bool anySelectedServerRunning = false;
                List<string> newlyRunningServers = new List<string>();

                // 检查所有区域的所有服务器
                foreach (var area in root.GetProperty("Data").EnumerateArray()) {
                    var areaName = area.GetProperty("AreaName").GetString();

                    foreach (var group in area.GetProperty("Group").EnumerateArray()) {
                        var serverName = group.GetProperty("name").GetString();
                        var isRunning = group.GetProperty("runing").GetBoolean();

                        // 更新UI中的服务器状态
                        foreach (TreeNode areaNode in serverTreeView.Nodes) {
                            if (areaNode.Text == areaName) {
                                foreach (TreeNode serverNode in areaNode.Nodes) {
                                    var server = serverNode.Tag as Server;
                                    if (server != null && server.Name == serverName) {
                                        // 更新服务器状态
                                        bool wasRunning = server.IsRunning;
                                        server.IsRunning = isRunning;

                                        // 更新节点显示
                                        UpdateServerNodeText(serverNode, server);

                                        // 检查是否需要记录并提醒
                                        if (server.IsSelected && isRunning) {
                                            anySelectedServerRunning = true;

                                            // 如果状态从未运行变为运行中，则记录日志并加入通知列表
                                            if (!wasRunning) {
                                                string message = $"{areaName} - {serverName} 服务开始运行!";
                                                logListBox.Items.Insert(0, $"[{DateTime.Now}] {message}");
                                                newlyRunningServers.Add(message);
                                            }
                                        }

                                        break;
                                    }
                                }

                                break;
                            }
                        }
                    }
                }

                // 如果有新启动的服务器，则播放提示音并显示通知
                if (newlyRunningServers.Count > 0) {
                    try {
                        alertSound?.Play();

                        // 准备通知消息
                        string notificationTitle = "服务器可用提醒";
                        string notificationText = string.Join("\n", newlyRunningServers);

                        // 显示气泡通知
                        notifyIcon.BalloonTipTitle = notificationTitle;
                        notifyIcon.BalloonTipText = notificationText;
                        notifyIcon.BalloonTipIcon = ToolTipIcon.Info;
                        notifyIcon.ShowBalloonTip(30000); // 显示30秒
                    } catch (Exception ex) {
                        logListBox.Items.Insert(0, $"[{DateTime.Now}] 播放提示音失败: {ex.Message}");
                    }
                }

                // 更新最后检查时间
                lastCheckLabel.Text = $"最后检查: {DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")}";
            } catch (Exception ex) {
                logListBox.Items.Insert(0, $"[{DateTime.Now}] 监控错误: {ex.Message}");
            }
        }

        private void ToggleButton_Click(object sender, EventArgs e) {
            ToggleMonitoring();
        }

        private void ToggleMonitoring() {
            isMonitoring = !isMonitoring;

            if (isMonitoring) {
                // 开启监控
                monitorTimer.Start();
                toggleButton.Text = "停止监控";
                toggleMonitorMenuItem.Text = "停止监控";
                statusLabel.Text = "监控状态: 已开启";
                logListBox.Items.Insert(0, $"[{DateTime.Now}] 开始监控服务器状态 (每10秒检查一次)");
            } else {
                // 停止监控
                monitorTimer.Stop();
                toggleButton.Text = "开始监控";
                toggleMonitorMenuItem.Text = "开始监控";
                statusLabel.Text = "监控状态: 未开启";
                logListBox.Items.Insert(0, $"[{DateTime.Now}] 停止监控服务器状态");
            }

            // 更新托盘图标状态
            UpdateTrayIconStatus();
        }

        private void UpdateTrayIconStatus() {
            // 更新托盘图标的提示文字，显示监控状态
            notifyIcon.Text = $"服务监控工具 - {(isMonitoring ? "监控中" : "未监控")}";
        }

        private void serverTreeView_AfterCheck(object sender, TreeViewEventArgs e) {
            // 如果用户选中/取消选中了一个节点
            if (e.Action == TreeViewAction.ByMouse || e.Action == TreeViewAction.ByKeyboard) {
                if (e.Node.Tag is Server server) {
                    // 更新服务器选择状态
                    server.IsSelected = e.Node.Checked;
                } else if (e.Node.Tag is ServerArea) {
                    // 如果是区域节点，则更新其下所有服务器节点
                    foreach (TreeNode serverNode in e.Node.Nodes) {
                        serverNode.Checked = e.Node.Checked;
                        if (serverNode.Tag is Server childServer) {
                            childServer.IsSelected = e.Node.Checked;
                        }
                    }
                }
            }
        }

        private void refreshButton_Click(object sender, EventArgs e) {
            // 手动刷新服务器列表
            _ = LoadServersAsync();
        }

        // 窗体大小改变事件 - 最小化到托盘
        private void Form1_Resize(object sender, EventArgs e) {
            if (WindowState == FormWindowState.Minimized) {
                // 隐藏窗体
                Hide();

                // 显示通知气泡
                notifyIcon.BalloonTipTitle = "服务监控工具";
                notifyIcon.BalloonTipText = "程序已最小化到系统托盘，将在后台继续监控服务器状态。";
                notifyIcon.BalloonTipIcon = ToolTipIcon.Info;
                notifyIcon.ShowBalloonTip(3000); // 显示3秒
            }
        }

        // 窗体关闭事件
        private void Form1_FormClosing(object sender, FormClosingEventArgs e) {
            if (!closeAppOnFormClosing) {
                e.Cancel = true; // 取消关闭

                // 隐藏窗体
                Hide();

                // 显示通知气泡
                notifyIcon.BalloonTipTitle = "服务监控工具";
                notifyIcon.BalloonTipText = "程序已最小化到系统托盘，将在后台继续监控服务器状态。\n如需完全退出，请右键托盘图标选择\"退出\"。";
                notifyIcon.BalloonTipIcon = ToolTipIcon.Info;
                notifyIcon.ShowBalloonTip(5000); // 显示5秒
            }
        }

        // 托盘图标双击事件
        private void notifyIcon_MouseDoubleClick(object sender, MouseEventArgs e) {
            if (e.Button == MouseButtons.Left) {
                Show();
                WindowState = FormWindowState.Normal;
                Activate(); // 激活窗口，使其获得焦点
            }
        }

        // 托盘菜单 - 显示主窗口
        private void showMenuItem_Click(object sender, EventArgs e) {
            Show();
            WindowState = FormWindowState.Normal;
            Activate();
        }

        // 托盘菜单 - 切换监控状态
        private void toggleMonitorMenuItem_Click(object sender, EventArgs e) {
            ToggleMonitoring();
        }

        // 托盘菜单 - 退出应用程序
        private void exitMenuItem_Click(object sender, EventArgs e) {
            // 关闭前确认
            if (isMonitoring) {
                var result = MessageBox.Show("监控正在进行中，确定要退出应用吗？", "确认退出",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question);

                if (result != DialogResult.Yes) {
                    return;
                }
            }

            // 标记为真正退出
            closeAppOnFormClosing = true;

            // 清理资源
            if (monitorTimer != null) {
                monitorTimer.Stop();
                monitorTimer.Dispose();
            }

            if (alertSound != null) {
                alertSound.Dispose();
            }

            if (httpClient != null) {
                httpClient.Dispose();
            }

            // 关闭应用
            Application.Exit();
        }
    }
}