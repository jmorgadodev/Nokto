using System.Text.Json.Serialization;
using Nokto.Core.Models;

namespace Nokto.Core.Serialization;

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
)]
[JsonSerializable(typeof(AppConfig))]
[JsonSerializable(typeof(AppSettings))]
[JsonSerializable(typeof(PresetsFile))]
[JsonSerializable(typeof(WorkflowActionItem))]
[JsonSerializable(typeof(SystemStatusState))]
[JsonSerializable(typeof(AuditLogEntry))]
[JsonSerializable(typeof(QuickPowerRequest))]
[JsonSerializable(typeof(PostponeRequest))]
[JsonSerializable(typeof(SystemMetrics))]
[JsonSerializable(typeof(BatteryStatus))]
[JsonSerializable(typeof(RemoteStatusSnapshot))]
[JsonSerializable(typeof(RemoteCommandResult))]
[JsonSerializable(typeof(ApplicationCloseTarget[]))]
public partial class NoktoJsonContext : JsonSerializerContext
{
}

[JsonSourceGenerationOptions(
    WriteIndented = false,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
)]
[JsonSerializable(typeof(AuditLogEntry))]
[JsonSerializable(typeof(SystemStatusState))]
public partial class NoktoCompactJsonContext : JsonSerializerContext
{
}
