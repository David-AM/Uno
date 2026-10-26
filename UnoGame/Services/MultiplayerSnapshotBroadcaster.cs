using Microsoft.AspNetCore.SignalR;
using UnoGame.Api.DTOs;
using UnoGame.Api.Hubs;

namespace UnoGame.Api.Services;

public static class MultiplayerSnapshotBroadcaster
{
    public static async Task BroadcastAsync(
        IMultiplayerLobbyService lobby,
        IHubContext<MultiplayerHub> hubContext,
        bool consumeEvents = true,
        CancellationToken cancellationToken = default,
        IReadOnlyList<(string ConnectionId, GameViewDto Game)>? snapshots = null)
    {
        await hubContext.Clients.Group(MultiplayerHub.Group)
            .SendAsync("lobbyUpdated", lobby.GetLobby(), cancellationToken);

        var gameSnapshots = snapshots ?? lobby.GetConnectedGameViews(consumeEvents);
        foreach (var snapshot in gameSnapshots)
            await hubContext.Clients.Client(snapshot.ConnectionId)
                .SendAsync("gameUpdated", snapshot.Game, cancellationToken);
    }
}
