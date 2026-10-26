namespace UnoGame.Api.Models;

public enum CardColor
{
    Red,
    Blue,
    Green,
    Yellow,
    Wild
}

public enum ActionType
{
    Skip,
    Reverse,
    DrawTwo,
    Wild,
    WildDrawFour
}

public enum TurnDirection
{
    Clockwise = 1,
    CounterClockwise = -1
}
