# UNO Multiplayer Project — Codex Handoff

Generated from the current workspace at the time of handoff. This document is intended to let a new Codex chat continue the work without relying on previous conversation context.

## Project overview

This is a multiplayer-only UNO application with two separately running applications:

```text
UnoGame.Client     React/Vite frontend, normally http://localhost:5173
UnoGame            ASP.NET Core backend, normally http://localhost:5124
```

The backend is one ASP.NET Core process containing both:

- REST/HTTP endpoints.
- A SignalR real-time hub.
- In-memory lobby, player presence, reconnect, forfeiture, and game state.

The frontend is currently a single-page React application. Most screens and game rendering are conditionally rendered from one `App` component rather than separate client routes.

## Current Git state

- Branch: `update`
- Tracking branch: `origin/update`
- HEAD: `0043255 Remove bot feature`
- Current working tree has uncommitted changes in:
  - `UnoGame.Client/src/App.jsx`
  - `UnoGame.Tests/MultiplayerPresenceTests.cs`
  - `UnoGame/Hubs/MultiplayerHub.cs`
  - `UnoGame/Models/GameController.cs`
  - `UnoGame/Services/MultiplayerLobbyService.cs`
- `CODEX_HANDOFF.md` is being added by this handoff request.

Do not assume the uncommitted changes are committed. Review the diff before committing or resetting anything.

## Important file paths

### Frontend

- [App.jsx](UnoGame.Client/src/App.jsx) — main React component, client state, SignalR connection, lobby/game rendering, actions, reconnection, SweetAlert, animations.
- [main.jsx](UnoGame.Client/src/main.jsx) — React entry point.
- [styles.css](UnoGame.Client/src/styles.css) — all current frontend styling, including table, opponent row, buttons, alerts, and animations.
- [package.json](UnoGame.Client/package.json) — Vite scripts and frontend dependencies.

### Backend startup and transport

- [Program.cs](UnoGame/Program.cs) — ASP.NET Core setup, CORS, singleton lobby service, hosted presence monitor, SignalR registration, route mapping.
- [MultiplayerHub.cs](UnoGame/Hubs/MultiplayerHub.cs) — SignalR methods and broadcasts.
- [GameController.cs](UnoGame/Controllers/GameController.cs) — REST controller under `/api/lobby`.
- [MultiplayerPresenceMonitor.cs](UnoGame/Services/MultiplayerPresenceMonitor.cs) — one-second background timer that expires disconnected players and pushes snapshots through `IHubContext`.

### Game and lobby state

- [MultiplayerLobbyService.cs](UnoGame/Services/MultiplayerLobbyService.cs) — singleton in-memory lobby/session/presence/reconnect state and service-to-hub game view generation.
- [GameController.cs](UnoGame/Models/GameController.cs) — UNO rules, deck, hands, turn direction, current player, scores, winners, forfeited-player filtering, and turn advancement.
- [Card.cs](UnoGame/Models/Card.cs) — card model and card/action/color definitions.
- [MultiplayerDtos.cs](UnoGame/DTOs/MultiplayerDtos.cs) — lobby, join, game view, player, and event DTOs.
- [GameDtos.cs](UnoGame/DTOs/GameDtos.cs) — remaining game DTO definitions.

### Tests and documentation

- [MultiplayerPresenceTests.cs](UnoGame.Tests/MultiplayerPresenceTests.cs) — lobby connection, reconnection, timeout, forfeiture, host transfer, and presence tests.
- [GameRulesTests.cs](UnoGame.Tests/GameRulesTests.cs) — card, turn, draw, reverse, UNO, winner, challenge, and deck-rule tests.
- [README.md](README.md) — project/API overview, but verify it against code before relying on it.

## Architecture and request flow

### REST/HTTP

The REST controller has the base route `/api/lobby` and exposes multiple endpoints, including lobby lookup, join, reconnect fallback, game actions, and restart. The DTO file does not define routes; it only defines request/response shapes.

The frontend uses `fetch()` for initial lobby/join operations and for a reconnect fallback. Normal gameplay is sent through SignalR.

### SignalR

SignalR is registered in [Program.cs](UnoGame/Program.cs) and mapped to:

```text
/hubs/multiplayer
```

The frontend constructs a `HubConnection` in [App.jsx](UnoGame.Client/src/App.jsx) with automatic reconnect delays `[0, 2000, 10000, 30000]`.

Client-to-server hub invocations include:

```text
Register
Start
Play
Draw
Pass
SayUno
CatchUno
ChallengeDrawFour
ChooseOpeningColor
LeaveLobby
ReturnToLobby
Restart
```

Server-to-client events include:

```text
lobbyUpdated
gameUpdated
actionError
```

The hub uses one SignalR group, `demo-lobby`, for the single in-memory lobby. It broadcasts lobby state to the group but sends each game snapshot to the individual connection, so a player receives their own hand while opponents receive only card counts.

The hub's `Register` method validates the player ID and reconnect token against the lobby service, stores the current `Context.ConnectionId`, adds the connection to the group, broadcasts state, and returns that player's game view.

On disconnect, `OnDisconnectedAsync` tells the lobby service to mark the connection disconnected and starts the reconnection deadline when a game is active.

## State ownership

The backend is authoritative. The singleton [MultiplayerLobbyService](UnoGame/Services/MultiplayerLobbyService.cs) owns the current lobby, players, host ID, connection states, reconnect tokens, queued events, round number, and active `GameController`.

The [GameController](UnoGame/Models/GameController.cs) owns UNO rules and game state such as:

- Hands and deck/discard piles.
- Current player index.
- Clockwise/counterclockwise direction.
- Draw penalties and challenge state.
- Round winner and match winner.
- Scores.
- Forfeited-player set.

The React `App` state is only a client projection used for rendering and local animation. It receives `gameUpdated` snapshots and stores them in `game`.

## Player identity and reconnect flow

When a player joins through `/api/lobby/join`, the server returns:

- Numeric `playerId`.
- Random `reconnectToken`.
- Lobby state.
- Game state if the game is already active.
- Whether the player is host.

Storage behavior in [App.jsx](UnoGame.Client/src/App.jsx):

- `sessionStorage['uno.playerId']` stores the player ID for the current browser tab/session.
- `sessionStorage['uno.reconnectToken']` stores the reconnect token for the current tab/session.
- `localStorage['uno.playerName']` stores the name so a newly opened tab can attempt name-based rejoin when the server still has an active game.

There are two recovery paths:

1. If session storage still exists, the client opens SignalR and invokes `Register(playerId, reconnectToken)`.
2. If the tab was closed and session storage disappeared, the client checks `/api/lobby`, reads the saved local name, and posts `/api/lobby/join` with that name. During an active game, the service matches the name case-insensitively to an existing player and returns the existing player ID/token/game view.

`RegisterConnection` rejects invalid tokens and forfeited players. Tokens remain valid after a normal round/match end so a client can return to the result screen, but a player who missed the forfeiture deadline is rejected.

## 60-second reconnection and forfeiture rules

`ReconnectGracePeriodSeconds` is 60 seconds. The active period is stored in each connection state as `DisconnectedAtUtc` and `DeadlineUtc`.

When a live-game connection disconnects:

- The player is marked disconnected.
- The match is paused while at least one non-forfeited player is disconnected.
- A reconnect deadline is recorded.
- The player remains in the player list during the grace period.

`MultiplayerPresenceMonitor` runs once per second. When a deadline expires, `ProcessExpiredDisconnections`:

1. Marks the player state as `IsForfeited`.
2. Calls `GameController.ForfeitPlayer`.
3. Keeps the player in server state so remaining hands, scores, and ordering are not rebuilt.
4. Skips the player for future turns.
5. Queues a dedicated `forfeit` event.
6. Transfers host ownership if the forfeited player was host.
7. Causes the monitor to broadcast updated lobby/game snapshots.

The client filters `isForfeited` players from the opponent row and from the `NEXT` calculation. The client prioritizes `forfeit` events in its animation queue and displays them with SweetAlert.

The current server event flow is intended to produce:

```text
forfeit SweetAlert
next eligible player's turn SweetAlert
updated player/turn rendering
```

## Two-player behavior

If one of two players forfeits:

- The remaining eligible player becomes the winner.
- The match status becomes `completed`.
- `OnGameEnded` is raised.
- The forfeited player remains present in the server's historical player list but is marked forfeited.
- The remaining player becomes host through `_hostPlayerId`.
- The result screen displays `Return to lobby` instead of `Next round`.

The remaining player must use `ReturnToLobby` to clear the old singleton session before either player can host or join a new lobby.

## Three-or-more-player behavior

If one or more players forfeit while at least two eligible players remain:

- The game continues in `playing` state.
- Forfeited players are excluded from turn selection.
- Remaining players keep their hands, scores, and game ordering.
- The current player advances immediately if the forfeited player was current.
- The existing direction is preserved unless a reverse card changes it.
- A forfeited host is replaced by the first eligible player in the existing player list for restart authorization.
- `Next round` remains available to the current host after a round completes.

The player list is intentionally not reordered when a host changes. Host ownership is tracked separately by `_hostPlayerId` so changing the host does not alter screen seating or turn ordering.

## Recent implementation changes

The current workspace includes these major changes from the earlier bot-era implementation:

- Removed the play-with-bot feature and its bot controller/service.
- Renamed the multiplayer controller to `GameController.cs` under `Controllers`.
- Kept the separate `Models/GameController.cs` for UNO rules; these are different classes/namespaces.
- Made the application multiplayer-only.
- Added duplicate-name validation for lobby joins.
- Added leave-lobby and return-to-lobby behavior.
- Added reconnect-token recovery and name-based rejoin fallback.
- Allowed reconnecting to an active result screen when the token is still valid.
- Added 60-second disconnect grace periods.
- Added forfeiture instead of removing disconnected players from the active game.
- Added eligible-player turn advancement and forfeited-player filtering.
- Added host transfer after the original host forfeits.
- Added dedicated forfeiture SweetAlert events.
- Added a regression test proving the remaining eligible player can start the next round after the host forfeits.

## Bugs already fixed

- Reverse-card direction changed visually but turn advancement continued using the original player array/order.
- Next-player display included forfeited players.
- Forfeited players remained visible in the opponent row.
- A forfeited host could block the next round because restart authorization depended on player index 0.
- Two-player forfeiture previously treated the remaining player as a generic round state instead of completing the match normally.
- Host/rejoin identity was previously inferred from the first player rather than an explicit host ID.
- The frontend did not require a name before hosting or joining.
- Duplicate names were allowed in some join paths.
- Result completion needed a return-to-lobby action to clear the old singleton game session.
- Forfeiture events could be hidden when the game status changed to completed; they now have a dedicated event type and client handling.

## Known risks and unresolved areas

These are the first areas to verify in a new debugging session:

1. **Repeated event delivery from the presence monitor.** `MultiplayerPresenceMonitor` calls `GetConnectedGameViews(consumeEvents: false)`, so queued events may be sent repeatedly on each one-second monitor tick until another operation consumes them. Verify whether forfeiture/turn SweetAlerts repeat or whether client queue deduplication is needed.
2. **Single global in-memory lobby.** The service is registered as a singleton and uses one hard-coded lobby ID, `demo-lobby`. Restarting the backend loses all players, tokens, and game state. Multiple independent lobbies are not supported.
3. **Name-based rejoin ambiguity.** A name is used as the fallback identity after a tab is closed. Names are unique within the current lobby, but name-based identity is weaker than preserving the original session token.
4. **Frontend monolith.** `App.jsx` contains connection lifecycle, API actions, game state, animations, alerts, lobby rendering, game rendering, and result rendering. A future refactor could extract hooks/components, but this was not done yet.
5. **Host semantics after all eligible players disappear.** The normal two-player path has a winner, but test any edge case where multiple players expire in the same monitor tick and verify `_hostPlayerId` is never left null while an ongoing match still has eligible players.
6. **Result/reconnect timing.** Test closing and reopening tabs immediately after a match ends, after `ReturnToLobby`, and after a player forfeits. Confirm that the intended result screen or landing screen is shown.
7. **Running-server build locks.** A running `UnoGame` process can lock the normal backend output DLL/EXE and make a normal `dotnet test` rebuild fail. Use a separate temporary `BaseOutputPath` or stop the development server before rebuilding; do not kill an unrelated process automatically.

## Exact next debugging steps

1. Start the backend and frontend from their respective projects.
2. Open three browser tabs and join as Host, Grace, and Lin.
3. Start the match and record the initial `lobbyUpdated` and `gameUpdated` payloads in browser devtools.
4. Disconnect the host tab while the match is active.
5. Confirm the other tabs show the paused state and a 60-second countdown.
6. Wait past 60 seconds without reconnecting the host.
7. Inspect the first `gameUpdated` payload after expiration:
   - Host has `isForfeited: true`.
   - Host is absent from the rendered opponent row.
   - `isCurrentPlayer` belongs to an eligible player.
   - `NEXT` skips the forfeited host.
   - `lobbyUpdated.hostPlayerId` is the promoted eligible player.
8. Confirm SweetAlert behavior:
   - Forfeit alert appears without requiring a draw.
   - Next-player alert appears after it.
   - Alerts do not repeat every second.
9. Complete the round and verify the promoted host can click `Next round`.
10. Repeat with only two players:
    - Disconnect the host.
    - Wait 60 seconds.
    - Confirm the remaining player wins and becomes host.
    - Confirm the result button is `Return to lobby`.
11. Click `Return to lobby` and verify the backend clears the previous session so the former player can host and the other player can join a new lobby.
12. Run validation from the repository root:

```powershell
dotnet test UnoGame.Tests/UnoGame.Tests.csproj --no-restore
```

If the running server locks the backend output, use a temporary output directory:

```powershell
dotnet test UnoGame.Tests/UnoGame.Tests.csproj --no-restore -p:BaseOutputPath=C:\path\outside-or-under-workspace\uno-validation\
```

Build the client from its project directory:

```powershell
Set-Location UnoGame.Client
npm.cmd run build
```

Finally, inspect the diff and ensure only the intended handoff/documentation or application changes are present:

```powershell
git status --short
git diff --check
git diff
```

## Important caution for the next chat

Do not edit the read-only snapshot attachments referenced in the previous conversation. Work only against the actual files in `C:\Users\Formulatrix\Documents\Uno`. Preserve existing uncommitted application changes unless the user explicitly requests a revert or reset.
