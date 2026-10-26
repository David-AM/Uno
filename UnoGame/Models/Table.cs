namespace UnoGame.Api.Models;

public interface IDeck { int TotalCards { get; } List<Card> Cards { get; } }

public sealed class Deck : IDeck
{
    public List<Card> Cards { get; } = [];
    public int TotalCards => Cards.Count;
    public Deck() { }
}

public interface IDiscardPile { List<Card> Cards { get; } }

public sealed class DiscardPile : IDiscardPile
{
    public List<Card> Cards { get; } = [];
}

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
