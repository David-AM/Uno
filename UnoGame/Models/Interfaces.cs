namespace UnoGame.Api.Models;

public interface ICard
{
    int Id { get; }
    CardColor Color { get; }
    ActionType? Action { get; }
    int? Value { get; }
    int ScoreValue { get; }
}

public interface IPlayer
{
    int Id { get; }
    string Name { get; }
}

public interface IDeck
{
    int TotalCards { get; }
    List<Card> Cards { get; }
}

public interface IDiscardPile
{
    List<Card> Cards { get; }
}

public interface ITable
{
    Deck DrawPile { get; }
    DiscardPile DiscardPile { get; }
    CardColor ActiveColor { get; set; }
}
