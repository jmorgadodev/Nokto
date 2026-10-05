using Nokto.Core.Models;

namespace Nokto.Core.Abstractions;

public interface IInstalledAppsService
{
    Task<IReadOnlyList<InstalledApplication>> ScanAsync(CancellationToken cancellationToken = default);
}
