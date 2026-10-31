using System.Text.Json;

namespace Nokto.Core.Services;

internal static class RemoteJson
{
    public static async Task<JsonDocument> ReadAsync(HttpResponseMessage response, CancellationToken cancellation)
    {
        const int limit = 1_000_000;
        if (response.Content.Headers.ContentLength > limit) throw new InvalidDataException("Respuesta del proveedor demasiado grande.");
        await using var stream = await response.Content.ReadAsStreamAsync(cancellation);
        using var buffer = new MemoryStream();
        byte[] block = new byte[8192]; int count;
        while ((count = await stream.ReadAsync(block, cancellation)) > 0)
        {
            if (buffer.Length + count > limit) throw new InvalidDataException("Respuesta del proveedor demasiado grande.");
            buffer.Write(block, 0, count);
        }
        return JsonDocument.Parse(buffer.ToArray());
    }
}
