using ServiceCheck.Models;

namespace ServiceCheck.Configuration;

/// <summary>
/// 表示服务器配置，包括多个区域和应用设置
/// </summary>
public class ServerConfiguration {
    /// <summary>
    /// 服务器区域列表，每个区域包含多个服务器
    /// </summary>
    public List<ServerArea> ServerAreas { get; init; } = [];

    /// <summary>
    /// 应用程序设置，例如是否启用提示音、连接超时时间等
    /// </summary>
    public AppSettings Settings { get; init; } = new();
}

/// <summary>
/// 表示应用程序的设置
/// </summary>
public class AppSettings {
    /// <summary>
    /// 是否启用提示音
    /// </summary>
    public static bool BeepEnabled => true;

    /// <summary>
    /// 是否启用连接超时提示
    /// </summary>
    public static int ConnectionTimeoutMs => 1000;
}