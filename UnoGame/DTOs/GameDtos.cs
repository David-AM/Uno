using UnoGame.Api.Models;

namespace UnoGame.Api.DTOs;

public sealed record CardDto(int Id, CardColor Color, ActionType? Action, int? Value, int ScoreValue)
{
    public static CardDto From(Card card) => new(card.Id, card.Color, card.Action, card.Value, card.ScoreValue);
}
public sealed record PlayerDto(int Id, string Name, int CardCount, bool IsCurrentPlayer, bool IsHuman, int Score,
    bool IsConnected = true, bool IsForfeited = false);
public sealed record GameEventDto(string Type, int? PlayerId, string? PlayerName, CardDto? Card, string Message, IReadOnlyList<CardDto>? Hand = null);
public sealed record GameViewDto(string GameId, string Status, string? Winner, int HumanPlayerId,
    IReadOnlyList<PlayerDto> Players, IReadOnlyList<CardDto> Hand, CardDto? TopCard, CardColor ActiveColor,
    int DrawPileCount, bool HasDrawnThisTurn, bool CanChallengeDrawFour, int PendingDrawCount, bool RequiresOpeningColorChoice, string Direction,
    IReadOnlyList<GameEventDto> RecentEvents, int RoundNumber,
    bool IsPaused = false, string? DisconnectedPlayer = null, int? ReconnectSecondsRemaining = null);
