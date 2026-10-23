# Table Club UNO

A multiplayer browser UNO game with a JavaScript/React view and an ASP.NET Core Web API plus SignalR lobby. Game rules live in the single C# game controller; the multiplayer service stores the active lobby in memory and returns only the requesting player's hand plus public opponent counts.

## Architecture

The domain follows the supplied [UNO class diagram](https://gist.github.com/vkenutama/58127dc1bd9c354b3a520384ff4aeab2):

| Diagram type | C# location | Responsibility |
| --- | --- | --- |
| `Card`, `ICard`, `CardColor`, `ActionType` | `UnoGame/Models/Card.cs` | Immutable card data and card enums |
| `Player`, `IPlayer` | `UnoGame/Models/Card.cs` | Player identity and name |
| `Deck`, `IDeck` | `UnoGame/Models/Card.cs` | Draw pile storage |
| `DiscardPile`, `IDiscardPile` | `UnoGame/Models/Card.cs` | Discarded cards |
| `Table`, `ITable` | `UnoGame/Models/Card.cs` | Draw/discard piles and active color |
| `GameController`, `TurnDirection` | `UnoGame/Models/GameController.cs` | Dealing, legality, effects, turn order, UNO and winner rules |
| Multiplayer lobby/session | `UnoGame/Services/MultiplayerLobbyService.cs` | In-memory player membership, reconnect handling and privacy-safe views |
| Lobby REST endpoints | `UnoGame/Controllers/GameController.cs` | Lobby join, game recovery and action responses |
| SignalR actions | `UnoGame/Hubs/MultiplayerHub.cs` | Real-time multiplayer actions and state broadcasts |
| Request/response shapes | `UnoGame/DTOs/MultiplayerDtos.cs` and `UnoGame/DTOs/GameDtos.cs` | API boundary without leaking private state |
| React view | `UnoGame.Client/src/` | Multiplayer lobby, card table, controls and responsive styling |
| Rule tests | `UnoGame.Tests/` | Independent C# rule checks |

The multiplayer service uses a single in-memory lobby, so the game resets when the API process restarts. SignalR broadcasts turns, plays, draws, alerts, reconnect state and lobby updates to all connected players.

## Run locally

Requirements: .NET 9 SDK and Node.js 20 or newer.

In one terminal, run the API:

```powershell
dotnet run --project .\UnoGame\UnoGame.csproj
```

In another terminal, run the React client:

```powershell
cd .\UnoGame.Client
npm install
npm run dev
```

Open the Vite URL shown in the client terminal (usually `http://localhost:5173`). The API base address is set in `UnoGame.Client/.env` as `VITE_API_URL`; adjust it if the API is hosted elsewhere. The API allows the local Vite origin through CORS.

## API

All responses are JSON. Invalid actions return `400` with an `error` message; missing games return `404`.

| Method | Route | Purpose |
| --- | --- | --- |
| `GET` | `/api/lobby` | Read lobby membership and connection state |
| `POST` | `/api/lobby/join` | Host or join a lobby; matching names can recover an active player |
| `GET` | `/api/lobby/game/{playerId}` | Recover a game using the reconnect token |

Gameplay actions are sent through the SignalR hub at `/hubs/multiplayer`. Draw Two and Wild Draw Four automatically make the next player draw the penalty cards and lose that turn. A player who goes from two cards to one without calling UNO first immediately draws four cards. The client presents alerts, animates cards toward the discard pile, and shows reconnect status. Wild Draw Four is restricted when the player holds a card matching the active color. Opponent hands and draw pile contents are never included in API responses.

## Tests

Run the xUnit project with:

```powershell
dotnet test .\UnoGame.Tests\UnoGame.Tests.csproj
```

The test project covers initial dealing/deck count, card matching, Wild color selection, and discard reshuffling.
