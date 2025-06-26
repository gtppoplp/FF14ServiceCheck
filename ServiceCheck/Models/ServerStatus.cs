using System;
using System.Collections.Generic;

namespace ServiceCheck.Models
{
    /// <summary>
    /// 表示一个服务器区域，包含多个服务器
    /// </summary>
    public class ServerArea
    {
        /// <summary>
        /// 区域名称
        /// </summary>
        public string Name { get; set; }
        
        /// <summary>
        /// 该区域下的服务器列表
        /// </summary>
        public List<Server> Servers { get; set; }
    }
    
    /// <summary>
    /// 表示一个服务器
    /// </summary>
    public class Server
    {
        /// <summary>
        /// 服务器名称
        /// </summary>
        public string Name { get; set; }
        
        /// <summary>
        /// 服务器是否正在运行
        /// </summary>
        public bool IsRunning { get; set; }
        
        /// <summary>
        /// 是否被选中进行监控
        /// </summary>
        public bool IsSelected { get; set; }
    }
}