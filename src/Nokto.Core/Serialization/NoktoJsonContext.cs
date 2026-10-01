using System.Text.Json.Serialization;
using Nokto.Core.Models;

namespace Nokto.Core.Serialization;

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
)]
[JsonSerializable(typeof(AppConfig))]
[JsonSerializable(typeof(PresetsFile))]
[JsonSerializable(typeof(SystemStatusState))]
[JsonSerializable(typeof(AuditLogEntry))]
[JsonSerializable(typeof(QuickPowerRequest))]
[JsonSerializable(typeof(PostponeRequest))]
[JsonSerializable(typeof(SystemMetrics))]
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
