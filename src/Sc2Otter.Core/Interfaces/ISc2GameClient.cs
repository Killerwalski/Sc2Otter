namespace Sc2Otter.Core.Interfaces;

using Sc2Otter.Core.Models;

public interface ISc2GameClient
{
    Task<Sc2GameResponse?> GetGameInfoAsync(CancellationToken ct = default);
    Task<Sc2UiResponse?> GetUiStateAsync(CancellationToken ct = default);
    Task<bool> IsGameRunningAsync(CancellationToken ct = default);
}
