using System.Buffers.Binary;
using System.Text;

namespace Nokto.Core.Services;

// Field numbers verified against the descriptors shipped in Antigravity's jetskiAgent/main.js.
// Decode only Topic/Row, Primitive and UserStatus -> ClientModelConfig -> QuotaInfo.
// Adjacent integers and model IDs are not percentages.
internal static class AntigravityStateDecoder
{
    private sealed record Field(int Number, int Wire, ulong Integer, ReadOnlyMemory<byte> Bytes);

    public static void Read(string key, byte[] bytes, DateTimeOffset observedAt, AiLocalQuotaTelemetry telemetry)
    {
        telemetry.ReadText(Encoding.UTF8.GetString(bytes), observedAt);
        byte[]? decoded = DecodeBase64(bytes);
        if (decoded != null)
        {
            telemetry.ReadText(Encoding.UTF8.GetString(decoded), observedAt);
            bytes = decoded;
        }
        if (key is not "antigravityUnifiedStateSync.modelCredits" and not "antigravityUnifiedStateSync.userStatus") return;
        var topic = Parse(bytes);
        if (topic == null) return;
        foreach (var entryField in topic.Where(f => f.Number == 1 && f.Wire == 2))
        {
            var entry = Parse(entryField.Bytes);
            var nameBytes = Get(entry, 1, 2);
            var row = Parse(Get(entry, 2, 2));
            var rowValue = Get(row, 1, 2);
            if (nameBytes.Length is 0 or > 128 || rowValue.IsEmpty) continue;
            string name = Encoding.UTF8.GetString(nameBytes.Span);
            var payload = DecodeBase64(rowValue.Span);
            if (payload == null) continue;
            if (key.EndsWith(".modelCredits", StringComparison.Ordinal))
            {
                var primitive = Parse(payload);
                var number = primitive?.FirstOrDefault(f => f.Number == 2 && f.Wire == 0);
                if (number == null || number.Integer > int.MaxValue) continue;
                if (name == "availableCreditsSentinelKey") telemetry.SetCounter("Créditos IA disponibles", number.Integer, observedAt);
                if (name == "minimumCreditAmountForUsageKey") telemetry.SetCounter("Créditos mínimos por uso", number.Integer, observedAt);
            }
            else if (name == "userStatusSentinelKey") ReadUserStatus(payload, observedAt, telemetry);
        }
    }

    private static void ReadUserStatus(byte[] bytes, DateTimeOffset observedAt, AiLocalQuotaTelemetry telemetry)
    {
        var status = Parse(bytes);
        var models = Parse(Get(status, 33, 2));
        if (models == null) return;
        foreach (var field in models.Where(f => f.Number == 1 && f.Wire == 2))
        {
            var model = Parse(field.Bytes);
            var labelBytes = Get(model, 1, 2);
            var quotaBytes = Get(model, 15, 2);
            if (labelBytes.Length is 0 or > 256 || quotaBytes.IsEmpty) continue;
            string label = Encoding.UTF8.GetString(labelBytes.Span);
            if (!label.Contains("gemini", StringComparison.OrdinalIgnoreCase) &&
                !label.Contains("claude", StringComparison.OrdinalIgnoreCase) && !label.Contains("gpt", StringComparison.OrdinalIgnoreCase)) continue;
            var quota = Parse(quotaBytes);
            if (quota == null || quota.Any(f => f.Number == 1 && f.Wire != 5)) continue;
            // An omitted scalar in a present proto3 QuotaInfo is the real default 0.
            var fractionBytes = Get(quota, 1, 5);
            double fraction = fractionBytes.IsEmpty ? 0 : BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(fractionBytes.Span));
            var timestamp = Parse(Get(quota, 2, 2));
            var seconds = timestamp?.FirstOrDefault(f => f.Number == 1 && f.Wire == 0);
            if (seconds == null || seconds.Integer > 253402300799) continue;
            var reset = DateTimeOffset.FromUnixTimeSeconds((long)seconds.Integer);
            // This contract has no window duration. Weekly / 5h require explicit bucket metadata.
            telemetry.Add(label, "Cuota", fraction * 100, observedAt, reset);
        }
    }

    private static ReadOnlyMemory<byte> Get(List<Field>? fields, int number, int wire) =>
        fields?.FirstOrDefault(f => f.Number == number && f.Wire == wire)?.Bytes ?? ReadOnlyMemory<byte>.Empty;

    private static byte[]? DecodeBase64(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length is 0 or > 1048576) return null;
        string text = Encoding.UTF8.GetString(bytes).Trim();
        if (text.Length > 1 && text[0] == '"' && text[^1] == '"') text = text[1..^1];
        try { return Convert.FromBase64String(text.PadRight(text.Length + (4 - text.Length % 4) % 4, '=')); }
        catch (FormatException) { return null; }
    }

    private static List<Field>? Parse(ReadOnlyMemory<byte> memory)
    {
        if (memory.IsEmpty) return [];
        var bytes = memory.Span;
        var fields = new List<Field>();
        int position = 0;
        while (position < bytes.Length && fields.Count < 4096)
        {
            if (!Varint(bytes, ref position, out ulong tag) || tag >> 3 is 0 or > 536870911) return null;
            int wire = (int)(tag & 7);
            ulong integer = 0;
            int length;
            if (wire == 0)
            {
                if (!Varint(bytes, ref position, out integer)) return null;
                length = 0;
            }
            else if (wire == 2)
            {
                if (!Varint(bytes, ref position, out ulong size) || size > (ulong)(bytes.Length - position)) return null;
                length = (int)size;
            }
            else if (wire is 1 or 5) length = wire == 1 ? 8 : 4;
            else return null;
            if (length > bytes.Length - position) return null;
            fields.Add(new Field((int)(tag >> 3), wire, integer, memory.Slice(position, length)));
            position += length;
        }
        return position == bytes.Length ? fields : null;
    }

    private static bool Varint(ReadOnlySpan<byte> bytes, ref int position, out ulong value)
    {
        value = 0;
        for (int shift = 0; shift < 70 && position < bytes.Length; shift += 7)
        {
            byte current = bytes[position++];
            if (shift == 63 && current > 1) return false;
            value |= (ulong)(current & 127) << shift;
            if (current < 128) return true;
        }
        return false;
    }
}
