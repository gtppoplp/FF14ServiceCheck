namespace ServiceCheck.Models {
    /// <summary>
    /// 表示一个服务器区域，包含多个服务器
    /// </summary>
    public class ServerArea {
        /// <summary>
        /// 区域名称
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// 该区域下的服务器列表
        /// </summary>
        public List<Server> Servers { get; }

        /// <summary>
        /// 构造函数，初始化服务器区域
        /// </summary>
        /// <param name="name"></param>
        /// <param name="servers"></param>
        public ServerArea(string name, List<Server> servers) {
            Name = name;
            Servers = servers;
        }
    }

    /// <summary>
    /// 表示一个服务器
    /// </summary>
    public class Server {
        /// <summary>
        /// 服务器名称
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// 服务器IP地址
        /// </summary>
        public string IpAddress { get; }

        /// <summary>
        /// 服务器端口
        /// </summary>
        public int Port { get; }

        /// <summary>
        /// 服务器是否正在运行
        /// </summary>
        public bool IsRunning { get; set; }

        /// <summary>
        /// 是否被选中进行监控
        /// </summary>
        public bool IsSelected { get; set; }

        /// <summary>
        /// 最后一次连接的延迟时间（毫秒）
        /// </summary>
        public double LastPingTime { get; set; }

        /// <summary>
        /// 构造函数，初始化服务器信息
        /// </summary>
        /// <param name="name"></param>
        /// <param name="ipAddress"></param>
        /// <param name="port"></param>
        /// <param name="isRunning"></param>
        /// <param name="isSelected"></param>
        /// <param name="lastPingTime"></param>
        public Server(string name, string ipAddress, int port, bool isRunning, bool isSelected, double lastPingTime) {
            Name = name;
            IpAddress = ipAddress;
            Port = port;
            IsRunning = isRunning;
            IsSelected = isSelected;
            LastPingTime = lastPingTime;
        }
    }
}