namespace UnoGame.Api.Models;

public interface ICard
{
    int Id { get; }
    CardColor Color { get; }
    ActionType? Action { get; }
    int? Value { get; }
    int ScoreValue { get; }
}
