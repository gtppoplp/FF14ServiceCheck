using System.Diagnostics;
using System.Media;
using System.Net;
using System.Net.Http;
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
    /// HTTP客户端，用于调用FF14官方API
    /// </summary>
    private readonly HttpClient httpClient;

    /// <summary>
    /// 最后一次API检查的结果缓存
    /// </summary>
    private FF14ApiResponse? lastApiResponse;

    /// <summary>
    /// 构造函数，初始化窗体和相关组件
    /// </summary>
    public Form1() {
        InitializeComponent();
        
    // 初始化HTTP客户端
        httpClient = new HttpClient();
        httpClient.Timeout = TimeSpan.FromSeconds(10);
        httpClient.DefaultRequestHeaders.Add("User-Agent", "FF14ServerMonitor/1.0");
        
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

        // 为测试抓包按钮添加事件处理
        testPacketButton.Click -= TestPacketButton_Click!;
        testPacketButton.Click += TestPacketButton_Click!;

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
    /// 更新服务器节点的文本和颜色（支持详细状态显示）
    /// </summary>
    /// <param name="node"></param>
    /// <param name="server"></param>
    /// <param name="status"></param>
    /// <param name="detailedStatus"></param>
    private static void UpdateServerNodeText(TreeNode node, Server server, string? status = null, string? detailedStatus = null) {
        var displayStatus = status ?? (server.IsRunning ? "在线" : "离线");
        var pingText = server.LastPingTime > 0 ? $" ({server.LastPingTime:F1}ms)" : "";
        
    node.Text = $@"{server.Name} [{displayStatus}]{pingText}";
        
        // 设置颜色和工具提示
        switch (displayStatus) {
            case "在线":
                node.ForeColor = Color.Green;
         break;
      case "完全离线":
         node.ForeColor = Color.Red;
       break;
  case "TCP通/API离线":
   node.ForeColor = Color.Orange;
                break;
            case "TCP断/API在线":
    node.ForeColor = Color.Purple;
      break;
      default:
        node.ForeColor = server.IsRunning ? Color.Green : Color.Red;
         break;
        }
        
        // 设置工具提示显示详细状态
        if (!string.IsNullOrEmpty(detailedStatus)) {
       node.ToolTipText = detailedStatus;
        }
    }

    /// <summary>
    /// 检查服务器的连接状态并进行抓包模拟
    /// </summary>
    /// <param name="server"></param>
    /// <returns></returns>
    private async Task<bool> CheckServerConnectivityAsync(Server server) {
        var stopwatch = Stopwatch.StartNew();
        TcpClient? client = null;
        NetworkStream? stream = null;
        
        try {
            logListBox.Items.Insert(0, $"[{DateTime.Now}] === 开始检查服务器 {server.Name} ({server.IpAddress}:{server.Port}) ===");
            
            client = new TcpClient();
            
            // 设置TCP选项
            client.ReceiveTimeout = AppSettings.ConnectionTimeoutMs;
            client.SendTimeout = AppSettings.ConnectionTimeoutMs;
            
            logListBox.Items.Insert(0, $"[{DateTime.Now}] [{server.Name}] 正在建立TCP连接...");
            
            // 设置连接超时
            var connectTask = client.ConnectAsync(IPAddress.Parse(server.IpAddress), server.Port);
            var timeoutTask = Task.Delay(AppSettings.ConnectionTimeoutMs);

            var completedTask = await Task.WhenAny(connectTask, timeoutTask);
            
            if (completedTask == timeoutTask) {
                logListBox.Items.Insert(0, $"[{DateTime.Now}] [{server.Name}] 连接超时 ({AppSettings.ConnectionTimeoutMs}ms)");
                server.LastPingTime = 0;
                return false;
            }
            
            if (!client.Connected) {
                logListBox.Items.Insert(0, $"[{DateTime.Now}] [{server.Name}] 连接失败 - 无法建立TCP连接");
                server.LastPingTime = 0;
                return false;
            }

            var connectTime = stopwatch.Elapsed.TotalMilliseconds;
            logListBox.Items.Insert(0, $"[{DateTime.Now}] [{server.Name}] TCP连接成功 (耗时: {connectTime:F1}ms)");
            
            // 获取网络流进行数据交换
            stream = client.GetStream();
            
            // 模拟游戏客户端登录握手包 (这里使用FF14的初始连接包格式)
            var handshakePacket = CreateFF14HandshakePacket();
            
            logListBox.Items.Insert(0, $"[{DateTime.Now}] [{server.Name}] 发送握手数据包 ({handshakePacket.Length} 字节)");
            logListBox.Items.Insert(0, $"[{DateTime.Now}] [{server.Name}] 发送数据: {BitConverter.ToString(handshakePacket)}");
            
            // 发送握手包
            await stream.WriteAsync(handshakePacket, 0, handshakePacket.Length);
            
            // 等待服务器响应
            var responseBuffer = new byte[1024];
            var responseTask = stream.ReadAsync(responseBuffer, 0, responseBuffer.Length);
            var responseTimeoutTask = Task.Delay(5000); // 5秒响应超时
            
            var responseCompletedTask = await Task.WhenAny(responseTask, responseTimeoutTask);
            
            if (responseCompletedTask == responseTimeoutTask) {
                logListBox.Items.Insert(0, $"[{DateTime.Now}] [{server.Name}] 服务器响应超时 (5000ms)");
            } else {
                var bytesRead = await responseTask;
                stopwatch.Stop();
                
                if (bytesRead > 0) {
                    var responseData = new byte[bytesRead];
                    Array.Copy(responseBuffer, responseData, bytesRead);
                    
                    logListBox.Items.Insert(0, $"[{DateTime.Now}] [{server.Name}] 收到服务器响应 ({bytesRead} 字节)");
                    logListBox.Items.Insert(0, $"[{DateTime.Now}] [{server.Name}] 响应数据: {BitConverter.ToString(responseData)}");
                    
                    // 解析响应数据
                    AnalyzeServerResponse(server, responseData);
                    
                    server.LastPingTime = stopwatch.Elapsed.TotalMilliseconds;
                    logListBox.Items.Insert(0, $"[{DateTime.Now}] [{server.Name}] 服务器在线! 总响应时间: {server.LastPingTime:F1}ms");
                    return true;
                } else {
                    logListBox.Items.Insert(0, $"[{DateTime.Now}] [{server.Name}] 服务器关闭了连接 (收到0字节)");
                }
            }
            
            server.LastPingTime = stopwatch.Elapsed.TotalMilliseconds;
            return true; // 即使没有数据响应，只要能连接就认为服务器在线
            
        } catch (SocketException ex) {
            stopwatch.Stop();
            logListBox.Items.Insert(0, $"[{DateTime.Now}] [{server.Name}] Socket异常: {ex.Message} (错误代码: {ex.ErrorCode})");
            server.LastPingTime = 0;
            return false;
        } catch (Exception ex) {
            stopwatch.Stop();
            logListBox.Items.Insert(0, $"[{DateTime.Now}] [{server.Name}] 连接异常: {ex.Message}");
            server.LastPingTime = 0;
            return false;
        } finally {
            try {
                stream?.Close();
                client?.Close();
                logListBox.Items.Insert(0, $"[{DateTime.Now}] [{server.Name}] 连接已关闭");
            } catch (Exception ex) {
                logListBox.Items.Insert(0, $"[{DateTime.Now}] [{server.Name}] 关闭连接时出错: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// 创建FF14游戏的握手数据包
    /// </summary>
    /// <returns></returns>
    private static byte[] CreateFF14HandshakePacket() {
        // 这是一个模拟的FF14客户端握手包
        // 实际的FF14数据包格式可能不同，这里只是为了演示
        var packet = new List<byte>();
        
        // 添加包头 (4字节)
        packet.AddRange(BitConverter.GetBytes((uint)0x41A05252)); // 魔术字节
        
        // 添加包长度 (4字节)
        packet.AddRange(BitConverter.GetBytes((uint)28)); // 总长度28字节
        
        // 添加连接类型 (4字节)
        packet.AddRange(BitConverter.GetBytes((uint)0x00000001)); // 连接类型: 登录
        
        // 添加客户端版本 (4字节)
        packet.AddRange(BitConverter.GetBytes((uint)0x2024011)); // 版本号
        
        // 添加时间戳 (8字节)
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        packet.AddRange(BitConverter.GetBytes(timestamp));
        
        // 添加校验和 (4字节)
        var checksum = CalculateChecksum(packet.ToArray());
        packet.AddRange(BitConverter.GetBytes(checksum));
        
        return packet.ToArray();
    }
    
    /// <summary>
    /// 计算数据包校验和
    /// </summary>
    /// <param name="data"></param>
    /// <returns></returns>
    private static uint CalculateChecksum(byte[] data) {
        uint sum = 0;
        for (int i = 0; i < data.Length; i += 4) {
            if (i + 3 < data.Length) {
                sum ^= BitConverter.ToUInt32(data, i);
            }
        }
        return sum;
    }

    /// <summary>
    /// 分析服务器响应数据
    /// </summary>
    /// <param name="server"></param>
    /// <param name="responseData"></param>
    private void AnalyzeServerResponse(Server server, byte[] responseData) {
        try {
            if (responseData.Length < 4) {
                logListBox.Items.Insert(0, $"[{DateTime.Now}] [{server.Name}] 响应数据太短，无法解析");
                return;
            }
            
            // 解析响应包头
            var header = BitConverter.ToUInt32(responseData, 0);
            logListBox.Items.Insert(0, $"[{DateTime.Now}] [{server.Name}] 响应包头: 0x{header:X8}");
            
            if (responseData.Length >= 8) {
                var length = BitConverter.ToUInt32(responseData, 4);
                logListBox.Items.Insert(0, $"[{DateTime.Now}] [{server.Name}] 声明长度: {length} 字节");
            }

            switch (header) {
                // 检查是否为已知的FF14服务器响应
                case 0x41A05252: {
                    logListBox.Items.Insert(0, $"[{DateTime.Now}] [{server.Name}] 检测到FF14服务器响应包!");
                
                    if (responseData.Length >= 12) {
                        var responseType = BitConverter.ToUInt32(responseData, 8);
                        switch (responseType) {
                            case 0x00000001:
                                logListBox.Items.Insert(0, $"[{DateTime.Now}] [{server.Name}] 响应类型: 登录确认");
                                break;
                            case 0x00000002:
                                logListBox.Items.Insert(0, $"[{DateTime.Now}] [{server.Name}] 响应类型: 服务器状态");
                                break;
                            case 0x00000003:
                                logListBox.Items.Insert(0, $"[{DateTime.Now}] [{server.Name}] 响应类型: 维护中");
                                break;
                            default:
                                logListBox.Items.Insert(0, $"[{DateTime.Now}] [{server.Name}] 响应类型: 未知 (0x{responseType:X8})");
                                break;
                        }
                    }

                    break;
                }
                case 0x52A05241:
                    logListBox.Items.Insert(0, $"[{DateTime.Now}] [{server.Name}] 检测到服务器维护响应!");
                    break;
                default:
                    logListBox.Items.Insert(0, $"[{DateTime.Now}] [{server.Name}] 未知的服务器响应格式");
                    break;
            }
            
            // 如果数据足够长，尝试提取更多信息
            if (responseData.Length >= 16) {
                logListBox.Items.Insert(0, $"[{DateTime.Now}] [{server.Name}] 额外数据: {BitConverter.ToString(responseData, 12, Math.Min(8, responseData.Length - 12))}");
            }
            
        } catch (Exception ex) {
            logListBox.Items.Insert(0, $"[{DateTime.Now}] [{server.Name}] 解析响应数据时出错: {ex.Message}");
        }
    }

    /// <summary>
    /// 检查所有服务器的状态（使用双层检测）
    /// </summary>
    private async Task CheckAllServersAsync() {
        logListBox.Items.Insert(0, $"[{DateTime.Now}] 开始检查所有服务器状态（双层检测）...");

        // 首先获取API数据
        var apiResponse = await GetFF14ApiStatusAsync();
      
        var allServers = serverAreas.SelectMany(area => area.Servers).ToList();
      
        // 并发执行双层检测
        var checkTasks = allServers.Select(server => PerformDualCheckAsync(server, apiResponse));
        var results = await Task.WhenAll(checkTasks);

        // 更新UI
        foreach (TreeNode areaNode in serverTreeView.Nodes) {
            foreach (TreeNode serverNode in areaNode.Nodes) {
                if (serverNode.Tag is Server server) {
                    var result = results.First(r => r.Server == server);
                    UpdateServerNodeText(serverNode, server, result.Status, result.DetailedStatus);
                }
            }
        }

        var onlineCount = results.Count(r => r.Server.IsRunning);
        var totalCount = results.Length;
        logListBox.Items.Insert(0, $"[{DateTime.Now}] 双层检查完成: {onlineCount}/{totalCount} 服务器在线");
    }

    /// <summary>
    /// 定时器每次触发时检查选中的服务器状态（使用双层检测）
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

            logListBox.Items.Insert(0, $"[{DateTime.Now}] 开始双层检查 {selectedServers.Count} 个服务器状态...");

            // 记录每个服务器检查前的状态
            var serverStates = selectedServers.ToDictionary(s => s, s => s.IsRunning);

            // 首先获取API数据
            var apiResponse = await GetFF14ApiStatusAsync();

            // 并发检查所有选中的服务器
            var checkTasks = selectedServers.Select(server => PerformDualCheckAsync(server, apiResponse));
            var results = await Task.WhenAll(checkTasks);

            // 更新UI和处理状态变化
            foreach (var result in results) {
                // 更新UI中对应的节点
                foreach (TreeNode areaNode in serverTreeView.Nodes) {
                    foreach (TreeNode serverNode in areaNode.Nodes) {
                        if (serverNode.Tag != result.Server) continue;
                        UpdateServerNodeText(serverNode, result.Server, result.Status, result.DetailedStatus);
                        break;
                    }
                }

                // 如果服务器从离线变为在线，则记录并加入通知列表
                if (!serverStates[result.Server] && result.Server.IsRunning) {
                    var areaName = serverAreas.First(a => a.Servers.Contains(result.Server)).Name;
                    var message = $"{areaName} - {result.Server.Name} 服务器上线! ({result.Status})";
                    logListBox.Items.Insert(0, $"[{DateTime.Now}] {message}");
                    newlyOnlineServers.Add(message);
                }
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

            var onlineCount = results.Count(r => r.Server.IsRunning);
            logListBox.Items.Insert(0, $"[{DateTime.Now}] 双层检查完成: {onlineCount}/{selectedServers.Count} 服务器在线");
     
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
        httpClient?.Dispose();

        Application.Exit();
    }

    /// <summary>
    /// 测试抓包按钮点击事件处理（使用双层检测）
    /// </summary>
 /// <param name="sender"></param>
    /// <param name="e"></param>
    private async void TestPacketButton_Click(object sender, EventArgs e) {
        try {
            // 获取当前选中的服务器节点
            var selectedNode = serverTreeView.SelectedNode;
     
         if (selectedNode?.Tag is not Server selectedServer) {
              MessageBox.Show(@"请先选择一个服务器进行测试。", @"提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
        return;
  }

testPacketButton.Enabled = false;
      testPacketButton.Text = @"测试中...";
    
   logListBox.Items.Insert(0, $"[{DateTime.Now}] ==========================================");
            logListBox.Items.Insert(0, $"[{DateTime.Now}] 开始双层测试服务器 {selectedServer.Name}");
          logListBox.Items.Insert(0, $"[{DateTime.Now}] ==========================================");
            
         // 获取API数据
            var apiResponse = await GetFF14ApiStatusAsync();
            
   // 执行双层检查
            var result = await PerformDualCheckAsync(selectedServer, apiResponse);
   
            // 更新UI中对应的节点
    foreach (TreeNode areaNode in serverTreeView.Nodes) {
      foreach (TreeNode serverNode in areaNode.Nodes) {
          if (serverNode.Tag != selectedServer) continue;
         UpdateServerNodeText(serverNode, selectedServer, result.Status, result.DetailedStatus);
        break;
          }
            }
            
      logListBox.Items.Insert(0, $"[{DateTime.Now}] ==========================================");
   logListBox.Items.Insert(0, $"[{DateTime.Now}] 双层测试完成!");
   logListBox.Items.Insert(0, $"[{DateTime.Now}] 最终状态: {result.Status}");
        logListBox.Items.Insert(0, $"[{DateTime.Now}] 详细信息: {result.DetailedStatus}");
 logListBox.Items.Insert(0, $"[{DateTime.Now}] ==========================================");
     
        } catch (Exception ex) {
          logListBox.Items.Insert(0, $"[{DateTime.Now}] 测试双层检测失败: {ex.Message}");
            MessageBox.Show($@"测试双层检测失败: {ex.Message}", @"错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
        } finally {
            testPacketButton.Enabled = true;
   testPacketButton.Text = @"测试抓包";
     }
    }

    /// <summary>
    /// 检查字节数组是否包含指定序列
    /// </summary>
    /// <param name="source"></param>
    /// <param name="pattern"></param>
    /// <returns></returns>
    private static bool ContainsSequence(byte[] source, byte[] pattern) {
        for (int i = 0; i <= source.Length - pattern.Length; i++) {
            bool found = true;
     for (int j = 0; j < pattern.Length; j++) {
   if (source[i + j] != pattern[j]) {
        found = false;
          break;
                }
            }
if (found) return true;
        }
      return false;
    }

    /// <summary>
    /// 调用FF14官方API获取服务器状态
    /// </summary>
    /// <returns></returns>
    private async Task<FF14ApiResponse?> GetFF14ApiStatusAsync() {
        try {
        logListBox.Items.Insert(0, $"[{DateTime.Now}] 正在调用FF14官方API...");
     
            const string apiUrl = "https://ff14act.web.sdo.com/api/serverStatus/getServerStatus";
          var response = await httpClient.GetAsync(apiUrl);
            
       if (response.IsSuccessStatusCode) {
                var jsonContent = await response.Content.ReadAsStringAsync();
    logListBox.Items.Insert(0, $"[{DateTime.Now}] API调用成功，收到响应数据");
       
      var apiResponse = JsonSerializer.Deserialize<FF14ApiResponse>(jsonContent, new JsonSerializerOptions {
           PropertyNameCaseInsensitive = true
        });
           
      if (apiResponse?.IsSuccess == true) {
          logListBox.Items.Insert(0, $"[{DateTime.Now}] API数据解析成功，共 {apiResponse.Data?.Count ?? 0} 个区域");
                lastApiResponse = apiResponse;
        return apiResponse;
    } else {
logListBox.Items.Insert(0, $"[{DateTime.Now}] API返回错误: {apiResponse?.ErrorMsg ?? "未知错误"}");
    }
   } else {
      logListBox.Items.Insert(0, $"[{DateTime.Now}] API调用失败: HTTP {response.StatusCode}");
        }
        } catch (HttpRequestException ex) {
            logListBox.Items.Insert(0, $"[{DateTime.Now}] API网络请求失败: {ex.Message}");
        } catch (TaskCanceledException ex) {
logListBox.Items.Insert(0, $"[{DateTime.Now}] API请求超时: {ex.Message}");
        } catch (JsonException ex) {
    logListBox.Items.Insert(0, $"[{DateTime.Now}] API响应解析失败: {ex.Message}");
        } catch (Exception ex) {
            logListBox.Items.Insert(0, $"[{DateTime.Now}] API调用异常: {ex.Message}");
        }
  
 return null;
    }

    /// <summary>
    /// 从API响应中查找特定服务器的状态
    /// </summary>
    /// <param name="serverName"></param>
    /// <param name="apiResponse"></param>
    /// <returns></returns>
    private static ServerStatusData? FindServerInApiResponse(string serverName, FF14ApiResponse? apiResponse) {
        if (apiResponse?.Data == null) return null;
        
        foreach (var area in apiResponse.Data) {
    var server = area.Group.FirstOrDefault(s => 
            string.Equals(s.Name, serverName, StringComparison.OrdinalIgnoreCase));
    if (server != null) {
 return server;
          }
}
    
        return null;
    }

    /// <summary>
 /// 执行双层服务器检测（TCP连接 + FF14 API）
 /// </summary>
 /// <param name="server"></param>
 /// <param name="apiResponse"></param>
 /// <returns></returns>
    private async Task<ServerCheckResult> PerformDualCheckAsync(Server server, FF14ApiResponse? apiResponse) {
     var result = new ServerCheckResult(server);
        
        logListBox.Items.Insert(0, $"[{DateTime.Now}] === 开始双层检测: {server.Name} ===");
     
     // 第一层：TCP连接检测
        logListBox.Items.Insert(0, $"[{DateTime.Now}] [{server.Name}] 第一层检测: TCP连接...");
        result.TcpConnectable = await CheckTcpConnectivityAsync(server);
        
        // 第二层：API状态检测
        logListBox.Items.Insert(0, $"[{DateTime.Now}] [{server.Name}] 第二层检测: 官方API状态...");
     result.ApiData = FindServerInApiResponse(server.Name, apiResponse);
 result.ApiReportsOnline = result.ApiData?.Running == true;
        
        // 分析双层检测结果
        AnalyzeDualCheckResult(result);
    
        logListBox.Items.Insert(0, $"[{DateTime.Now}] [{server.Name}] 最终状态: {result.Status}");
    logListBox.Items.Insert(0, $"[{DateTime.Now}] === 双层检测完成: {server.Name} ===");
     
        return result;
    }

    /// <summary>
    /// 分析双层检测结果并确定最终状态
    /// </summary>
    /// <param name="result"></param>
    private void AnalyzeDualCheckResult(ServerCheckResult result) {
        var server = result.Server;
        
        if (result.TcpConnectable && result.ApiReportsOnline) {
       // 两层检测都通过
   result.Status = "在线";
            result.DetailedStatus = "TCP连接正常，官方API确认在线";
   server.IsRunning = true;
        } else if (result.TcpConnectable && !result.ApiReportsOnline) {
        // TCP通但API报告离线
    result.Status = "TCP通/API离线";
            result.DetailedStatus = "TCP端口开放但官方API报告离线";
  server.IsRunning = false;
   } else if (!result.TcpConnectable && result.ApiReportsOnline) {
        // TCP不通但API报告在线
        result.Status = "TCP断/API在线";
     result.DetailedStatus = "TCP连接失败但官方API报告在线";
            server.IsRunning = false;
      } else {
// 两层检测都失败
            result.Status = "完全离线";
        result.DetailedStatus = "TCP连接失败且官方API确认离线";
            server.IsRunning = false;
        }
      
        // 添加API详细信息
  if (result.ApiData != null) {
          var apiDetails = new List<string>();
            if (result.ApiData.IsNew) apiDetails.Add("新服");
          if (result.ApiData.IsUpgrade) apiDetails.Add("维护中");
    if (result.ApiData.IsBusy) apiDetails.Add("繁忙");
       if (!result.ApiData.IsCreate) apiDetails.Add("禁止建角");
 if (!result.ApiData.IsInt) apiDetails.Add("禁止登录");
            
            if (apiDetails.Count > 0) {
                result.DetailedStatus += $" ({string.Join(", ", apiDetails)})";
        }
        }
        
        logListBox.Items.Insert(0, $"[{DateTime.Now}] [{server.Name}] TCP检测: {(result.TcpConnectable ? "成功" : "失败")}");
        logListBox.Items.Insert(0, $"[{DateTime.Now}] [{server.Name}] API检测: {(result.ApiReportsOnline ? "在线" : "离线")}");
        logListBox.Items.Insert(0, $"[{DateTime.Now}] [{server.Name}] 详细状态: {result.DetailedStatus}");
    }

    /// <summary>
    /// 简化的TCP连接检测（不包含复杂的数据分析）
    /// </summary>
    /// <param name="server"></param>
    /// <returns></returns>
    private async Task<bool> CheckTcpConnectivityAsync(Server server) {
        var stopwatch = Stopwatch.StartNew();
        
        try {
      using var client = new TcpClient();
         
            // 设置连接超时
    var connectTask = client.ConnectAsync(IPAddress.Parse(server.IpAddress), server.Port);
            var timeoutTask = Task.Delay(AppSettings.ConnectionTimeoutMs);

         var completedTask = await Task.WhenAny(connectTask, timeoutTask);
   stopwatch.Stop();
  
          if (completedTask == connectTask && client.Connected) {
            server.LastPingTime = stopwatch.Elapsed.TotalMilliseconds;
  logListBox.Items.Insert(0, $"[{DateTime.Now}] [{server.Name}] TCP连接成功 ({server.LastPingTime:F1}ms)");
      return true;
} else {
   server.LastPingTime = 0;
                logListBox.Items.Insert(0, $"[{DateTime.Now}] [{server.Name}] TCP连接失败 (超时)");
            return false;
            }
        } catch (Exception ex) {
  stopwatch.Stop();
            server.LastPingTime = 0;
  logListBox.Items.Insert(0, $"[{DateTime.Now}] [{server.Name}] TCP连接异常: {ex.Message}");
            return false;
        }
    }
}