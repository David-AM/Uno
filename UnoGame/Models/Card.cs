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

public interface IPlayer { int Id { get; } string Name { get; } }
public sealed class Player(int id, string name) : IPlayer
{
    public int Id { get; } = id;
    public string Name { get; set; } = name;
    public override string ToString() => Name;
}

public interface IDeck { int TotalCards { get; } List<Card> Cards { get; } }
public sealed class Deck : IDeck
{
    public List<Card> Cards { get; } = [];
    public int TotalCards => Cards.Count;
    public Deck() { }
}

public interface IDiscardPile { List<Card> Cards { get; } }
public sealed class DiscardPile : IDiscardPile { public List<Card> Cards { get; } = []; }

public interface ITable
{
    Deck DrawPile { get; }
    DiscardPile DiscardPile { get; }
    CardColor ActiveColor { get; set; }
}
public sealed class Table(Deck drawPile, DiscardPile discardPile) : ITable
{
    public Deck DrawPile { get; } = drawPile;
    public DiscardPile DiscardPile { get; } = discardPile;
    public CardColor ActiveColor { get; set; }
}
