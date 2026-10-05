namespace Nokto.Core.Models;

/// <summary>Active audio endpoint identity; IDs distinguish devices with identical names.</summary>
public sealed record AudioEndpointInfo(string Id, string Name, bool IsDefault)
{
    public override string ToString() => Name;
}
