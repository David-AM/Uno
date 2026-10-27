using UnoGame.Api.DTOs;
using UnoGame.Api.Models;

namespace UnoGame.Api.Services;
public interface IMultiplayerLobbyService
{
    LobbyResponse GetLobby();
    OperationResult<JoinLobbyResponse> Join(string playerName, int opponentCount, bool hostLobby);
    OperationResult<GameViewDto> Start(int playerId);
    GameViewDto? GetGame(int playerId);
    OperationResult<GameViewDto> Play(int playerId, int cardId, CardColor? chosenColor);
    OperationResult<GameViewDto> Draw(int playerId);
    OperationResult<GameViewDto> Pass(int playerId);
    OperationResult<GameViewDto> SayUno(int playerId);
    OperationResult<GameViewDto> CatchUno(int playerId, int targetPlayerId);
    OperationResult<GameViewDto> ChallengeDrawFour(int playerId);
    OperationResult<GameViewDto> ChooseOpeningColor(int playerId, CardColor color);
    OperationResult<bool> LeaveLobby(int playerId);
    OperationResult<bool> ReturnToLobby(int playerId);
    bool RegisterConnection(int playerId, string reconnectToken, string connectionId, out string error);
    bool IsReconnectTokenValid(int playerId, string reconnectToken);
    bool IsConnectionAuthorized(int playerId, string connectionId);
    bool DisconnectConnection(string connectionId);
    bool ProcessExpiredDisconnections();
    IReadOnlyList<ConnectedGameSnapshot> GetConnectedGameViews(bool consumeEvents = true);
    GameViewDto? Restart(int playerId, bool resetScores = false);
}