namespace UnoGame.Api.Models;

public interface IDeck
{
    int TotalCards { get; }
    List<Card> Cards { get; }
}
