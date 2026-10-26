namespace UnoGame.Api.DTOs;

public sealed class OperationResult<T>
{
    private OperationResult(T? result, string? error)
    {
        Result = result;
        Error = error;
    }

    public T? Result { get; }
    public string? Error { get; }
    public bool Succeeded => Error is null;

    public static OperationResult<T> Success(T result)
    {
        return new OperationResult<T>(result, null);
    }

    public static OperationResult<T> Failure(string error)
    {
        return new OperationResult<T>(default, error);
    }
}

public sealed record ConnectedGameSnapshot(string ConnectionId, GameViewDto Game);
