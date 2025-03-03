using System.Threading;
using System.Threading.Tasks;

namespace SS14.Launcher.Models.EngineManager;
// This is an interface instead of a class because
// I was originally planning to make Steam builds bundle the engine with the Steam download.

/// <summary>
///     Manages engine installations.
/// </summary>
public interface IEngineManager
{
    string GetEnginePath(string engineVersion);
    string GetEngineSignature(string engineVersion);

    Task<bool> DownloadEngineIfNecessary(
        string engineVersion,
        Helpers.DownloadProgressCallback? progress = null,
        CancellationToken cancel = default);

    Task DoEngineCullMaybeAsync();
    void ClearAllEngines();
}