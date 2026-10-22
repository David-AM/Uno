using Microsoft.AspNetCore.SignalR;
using UnoGame.Api.Hubs;

namespace UnoGame.Api.Services;

public sealed class MultiplayerPresenceMonitor(
    IMultiplayerLobbyService lobby,
    IHubContext<MultiplayerHub> hubContext,
    ILogger<MultiplayerPresenceMonitor> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                var expired = lobby.ProcessExpiredDisconnections();
                var snapshots = lobby.GetConnectedGameViews(consumeEvents: false);
                if (expired || snapshots.Count > 0)
                {
                    await hubContext.Clients.Group(MultiplayerHub.Group)
                        .SendAsync("lobbyUpdated", lobby.GetLobby(), stoppingToken);
                    foreach (var snapshot in snapshots)
                        await hubContext.Clients.Client(snapshot.ConnectionId)
                            .SendAsync("gameUpdated", snapshot.Game, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
            catch (Exception exception) { logger.LogError(exception, "Error while monitoring multiplayer presence."); }
        }
    }
}
