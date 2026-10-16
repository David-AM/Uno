# Table Club UNO

A browser UNO game with a JavaScript/React view and an ASP.NET Core Web API game engine. Game rules live in the C# domain layer; the API stores active games in memory and returns only the human player's hand plus public opponent counts.

## Architecture

The domain follows the supplied [UNO class diagram](https://gist.github.com/vkenutama/58127dc1bd9c354b3a520384ff4aeab2):

| Diagram type | C# location | Responsibility |
| --- | --- | --- |
| `Card`, `ICard`, `CardColor`, `ActionType` | `UnoGame/Models/Card.cs` | Immutable card data and card enums |
| `Player`, `IPlayer` | `UnoGame/Models/Card.cs` | Player identity and name |
| `Deck`, `IDeck` | `UnoGame/Models/Card.cs` | Draw pile storage |
| `DiscardPile`, `IDiscardPile` | `UnoGame/Models/Card.cs` | Discarded cards |
| `Table`, `ITable` | `UnoGame/Models/Card.cs` | Draw/discard piles and active color |
| `Lobby` | `UnoGame/Models/Lobby.cs` | Diagram-compatible lobby membership and start lifecycle |
| `GameController`, `TurnDirection` | `UnoGame/Models/GameController.cs` | Dealing, legality, effects, turn order, UNO and winner rules |
| Game session, AI | `UnoGame/Services/GameService.cs` | In-memory lookup, human/AI orchestration and privacy-safe views |
| REST endpoints | `UnoGame/Controllers/GamesController.cs` | Request validation and HTTP responses |
| Request/response shapes | `UnoGame/DTOs/GameDtos.cs` | API boundary without leaking private state |
| React view | `UnoGame.Client/src/` | Start screen, card table, controls and responsive styling |
| Rule tests | `UnoGame.Tests/` | Independent C# rule checks |

REST needs a stable opaque game key, so the service uses a string game ID while preserving the diagram's integer IDs for players and lobbies. The React client is the View and the ASP.NET Core controllers are the Controller layer; game logic stays in the domain. The first version uses an in-memory singleton game service, so games reset when the API process restarts.

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
| `POST` | `/api/games` | Create a game. Body: `{ "playerName": "Ada", "opponentCount": 3 }` |
| `GET` | `/api/games/{gameId}` | Read the current public game view and the human hand |
| `POST` | `/api/games/{gameId}/play` | Play `{ "cardId": 5, "chosenColor": "Blue" }`; `chosenColor` is needed for Wild cards |
| `POST` | `/api/games/{gameId}/draw` | Draw a card or resolve a pending draw penalty |
| `POST` | `/api/games/{gameId}/pass` | End the human turn after drawing |
| `POST` | `/api/games/{gameId}/uno` | Declare UNO with one card left |
| `POST` | `/api/games/{gameId}/catch-uno` | Catch an opponent who has one card and did not declare |
| `POST` | `/api/games/{gameId}/restart` | Start a new round in the same game |

The AI plays legal cards, chooses its most common non-Wild color, draws when needed, and uses the same game controller as the human. Draw Two and Wild Draw Four automatically make the next player draw the penalty cards and lose that turn; the client announces this in the highlighted alert banner. Wild Draw Four is restricted when the player holds a card matching the active color. Opponent hands and the draw pile contents are never included in API responses.

## Tests

Run the xUnit project with:

```powershell
dotnet test .\UnoGame.Tests\UnoGame.Tests.csproj
```

The test project covers initial dealing/deck count, card matching, Wild color selection, and discard reshuffling.
