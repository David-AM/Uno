namespace UnoGame.Api.Models;

public enum CardColor { Red, Blue, Green, Yellow, Wild }
public enum ActionType { Skip, Reverse, DrawTwo, Wild, WildDrawFour }
public enum TurnDirection { Clockwise = 1, CounterClockwise = -1 }

public interface ICard
{
    int Id { get; }
    CardColor Color { get; }
    ActionType? Action { get; }
    int? Value { get; }
    int ScoreValue { get; }
}

public sealed record Card(int Id, int? Value, int ScoreValue, CardColor Color, ActionType? Action) : ICard
{
    public override string ToString() => Action?.ToString() ?? Value?.ToString() ?? "Wild";
}
