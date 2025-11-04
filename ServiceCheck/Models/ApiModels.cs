using System.Text.Json.Serialization;

namespace ServiceCheck.Models;

/// <summary>
/// FF14官方API响应根对象
/// </summary>
public class FF14ApiResponse
{
    [JsonPropertyName("IsSuccess")]
    public bool IsSuccess { get; set; }

    [JsonPropertyName("Data")]
    public List<ServerAreaData> Data { get; set; } = [];

    [JsonPropertyName("Errormsg")]
    public string ErrorMsg { get; set; } = string.Empty;

    [JsonPropertyName("Errorcode")]
    public int ErrorCode { get; set; }

    [JsonPropertyName("total")]
    public int Total { get; set; }
}

/// <summary>
/// 服务器区域数据
/// </summary>
public class ServerAreaData
{
    [JsonPropertyName("AreaName")]
    public string AreaName { get; set; } = string.Empty;

    [JsonPropertyName("Group")]
    public List<ServerStatusData> Group { get; set; } = [];
}

/// <summary>
/// 服务器状态数据
/// </summary>
public class ServerStatusData
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("runing")]
    public bool Running { get; set; }

    [JsonPropertyName("isnew")]
    public bool IsNew { get; set; }

    [JsonPropertyName("isint")]
    public bool IsInt { get; set; }

    [JsonPropertyName("isout")]
    public bool IsOut { get; set; }

    [JsonPropertyName("iscreate")]
    public bool IsCreate { get; set; }

    [JsonPropertyName("isupgrade")]
    public bool IsUpgrade { get; set; }

    [JsonPropertyName("isbusy")]
    public bool IsBusy { get; set; }
}

/// <summary>
/// 扩展的服务器检查结果
/// </summary>
public class ServerCheckResult
{
    public Server Server { get; set; }
    public bool TcpConnectable { get; set; }
    public bool ApiReportsOnline { get; set; }
    public ServerStatusData? ApiData { get; set; }
    public string Status { get; set; } = string.Empty;
    public string DetailedStatus { get; set; } = string.Empty;

    public ServerCheckResult(Server server)
    {
        Server = server;
    }
}