using UnoGame.Api.Models;

namespace UnoGame.Api.DTOs;

public sealed record CreateGameRequest(string PlayerName, int OpponentCount = 3);
public sealed record PlayCardRequest(int CardId, CardColor? ChosenColor);
public sealed record ChooseColorRequest(CardColor Color);
public sealed record CatchUnoRequest(int PlayerId);
public sealed record CardDto(int Id, CardColor Color, ActionType? Action, int? Value, int ScoreValue)
{
    public static CardDto From(Card card) => new(card.Id, card.Color, card.Action, card.Value, card.ScoreValue);
}
public sealed record PlayerDto(int Id, string Name, int CardCount, bool IsCurrentPlayer, bool IsHuman);
public sealed record PlayerPlayDto(int PlayerId, string PlayerName, CardDto Card);
public sealed record GameViewDto(string GameId, string Status, string? Winner, int HumanPlayerId,
    IReadOnlyList<PlayerDto> Players, IReadOnlyList<CardDto> Hand, CardDto? TopCard, CardColor ActiveColor,
    int DrawPileCount, bool HasDrawnThisTurn, string Direction, string? Message, string? AlertMessage,
    IReadOnlyList<PlayerPlayDto> RecentPlays);
