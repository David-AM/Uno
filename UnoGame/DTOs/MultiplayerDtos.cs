using System.ComponentModel.DataAnnotations;
using UnoGame.Api.Models;

namespace UnoGame.Api.DTOs;

/// <summary>Payload used to join the demonstration lobby.</summary>
public sealed record JoinLobbyRequest
{
    [Required, StringLength(24, MinimumLength = 1)]
    public required string PlayerName { get; init; }

    [Range(1, 3)]
    public int OpponentCount { get; init; } = 3;

    public bool HostLobby { get; init; }
}

/// <summary>Public state of the single demonstration lobby.</summary>
public sealed record LobbyResponse(string LobbyId, bool IsPlaying, int MinPlayers, int MaxPlayers,
    int? HostPlayerId, IReadOnlyList<LobbyPlayerResponse> Players);

/// <summary>A player visible in the lobby.</summary>
public sealed record LobbyPlayerResponse(int Id, string Name, bool IsConnected = true, int? ReconnectSecondsRemaining = null);

/// <summary>Result returned after a player joins.</summary>
public sealed record JoinLobbyResponse(int PlayerId, bool IsHost, LobbyResponse Lobby, GameViewDto? Game, string ReconnectToken);

/// <summary>Payload for playing a card in the multiplayer game.</summary>
public sealed record MultiplayerPlayRequest(int PlayerId, int CardId, CardColor? ChosenColor);

/// <summary>Payload identifying the player performing an action.</summary>
public sealed record MultiplayerPlayerRequest(int PlayerId);
public sealed record MultiplayerOpeningColorRequest(int PlayerId, CardColor Color);

/// <summary>Payload identifying a target for a UNO catch.</summary>
public sealed record MultiplayerCatchUnoRequest(int PlayerId, int TargetPlayerId);
