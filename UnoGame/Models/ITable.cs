namespace UnoGame.Api.Models;

public interface ITable
{
    Deck DrawPile { get; }
    DiscardPile DiscardPile { get; }
    CardColor ActiveColor { get; set; }
}
