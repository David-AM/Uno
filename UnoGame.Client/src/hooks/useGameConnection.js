import { useCallback, useEffect, useState } from "react";
import { HubConnectionBuilder, LogLevel } from "@microsoft/signalr";
import { HUB, request } from "../api/request.js";

export function terminalConnectionError(message) {
  return /forfeit|reconnection window/i.test(message || "")
    ? {
        status: "forfeited",
        message:
          "You missed the 60-second reconnection window. You have forfeited this match.",
      }
    : /match has ended|no longer available/i.test(message || "")
      ? { status: "match-ended", message: "This match has ended." }
      : null;
}

export function openHub({
  playerId,
  reconnectToken,
  onLobby,
  onGame,
  onError = () => {},
  onStatus = () => {},
  onTerminal = () => {},
}) {
  const hub = new HubConnectionBuilder()
    .withUrl(HUB)
    .withAutomaticReconnect([0, 2000, 10000, 30000])
    .configureLogging(LogLevel.Warning)
    .build();

  hub.on("lobbyUpdated", (state) => onLobby?.(state));
  hub.on("gameUpdated", (state) => onGame?.(state ?? null));
  hub.on("actionError", (message) => onError(message));

  let terminal = false;
  hub.onreconnecting(() => {
    if (!terminal) onStatus("reconnecting");
  });
  hub.onreconnected(async () => {
    try {
      const state = await hub.invoke("Register", playerId, reconnectToken);
      if (state) onGame?.(state);
      onStatus("connected");
    } catch (error) {
      const terminalState = terminalConnectionError(error.message);
      if (terminalState) {
        terminal = true;
        onGame?.(null);
        onTerminal(terminalState.message);
        onStatus(terminalState.status);
        hub.stop();
      } else {
        onError(error.message);
        onStatus("disconnected");
      }
    }
  });
  hub.onclose(() => {
    if (!terminal) onStatus("disconnected");
  });

  return hub;
}

export function useGameConnection({ error, setError } = {}) {
  const [game, setGame] = useState(null);
  const [lobby, setLobby] = useState(null);
  const [playerId, setPlayerId] = useState(null);
  const [connection, setConnection] = useState(null);
  const [connectionStatus, setConnectionStatus] = useState("offline");
  const [terminalMessage, setTerminalMessage] = useState("");
  const [rejoiningGame, setRejoiningGame] = useState(false);
  const [friendsRole, setFriendsRole] = useState(null);
  const [name, setName] = useState("");
  const [opponents, setOpponents] = useState(3);

  const returnToLobby = useCallback(async () => {
    if (connection && playerId && game?.status === "completed") {
      try {
        await connection.invoke("ReturnToLobby", playerId);
      } catch (e) {
        setError?.(e.message);
        return;
      }
    }
    connection?.stop();
    sessionStorage.removeItem("uno.playerId");
    sessionStorage.removeItem("uno.reconnectToken");
    localStorage.removeItem("uno.playerName");
    setConnection(null);
    setPlayerId(null);
    setLobby(null);
    setGame(null);
    setFriendsRole(null);
    setTerminalMessage("");
    setConnectionStatus("offline");
    setError?.("");
  }, [connection, game?.status, playerId, setError]);

  const leaveLobby = useCallback(async () => {
    if (!connection || !playerId) {
      returnToLobby();
      return;
    }

    try {
      await connection.invoke("LeaveLobby", playerId);
      returnToLobby();
    } catch (e) {
      setError?.(e.message);
    }
  }, [connection, playerId, returnToLobby, setError]);

  const hostOrJoin = useCallback(async () => {
    if (!name.trim()) {
      throw new Error("Enter your name before starting or hosting a game.");
    }

    setTerminalMessage("");
    const lobbyState = await request("/lobby");
    const normalizedName = name.trim().toLowerCase();
    const duplicateName = lobbyState.players.some(
      (player) => player.name.trim().toLowerCase() === normalizedName,
    );
    if (!lobbyState.isPlaying && duplicateName) {
      throw new Error(
        "That name is already in the player list. Choose another name.",
      );
    }

    const joined = await request("/lobby/join", "POST", {
      playerName: name.trim() || "You",
      opponentCount: opponents,
      hostLobby: friendsRole === "host",
    });

    setPlayerId(joined.playerId);
    setLobby(joined.lobby);
    sessionStorage.setItem("uno.playerId", String(joined.playerId));
    sessionStorage.setItem("uno.reconnectToken", joined.reconnectToken);
    localStorage.setItem("uno.playerName", name.trim());

    const hub = openHub({
      playerId: joined.playerId,
      reconnectToken: joined.reconnectToken,
      onLobby: setLobby,
      onGame: (state) => {
        if (state) setGame(state);
        else setGame(null);
      },
      onError: (message) => setError?.(message),
      onStatus: setConnectionStatus,
      onTerminal: (message) => {
        setGame(null);
        setTerminalMessage(message);
        setConnectionStatus("forfeited");
      },
    });

    try {
      await hub.start();
      const current = await hub.invoke(
        "Register",
        joined.playerId,
        joined.reconnectToken,
      );
      setConnectionStatus("connected");
      setConnection(hub);
      setGame(current || joined.game || null);
      return current || joined.game;
    } catch (error) {
      const terminalState = terminalConnectionError(error.message);
      if (terminalState) {
        setGame(null);
        setTerminalMessage(terminalState.message);
        setConnectionStatus(terminalState.status);
        hub.stop();
        return null;
      }
      throw error;
    }
  }, [friendsRole, name, opponents, setError]);

  useEffect(
    () => () => {
      connection?.stop();
    },
    [connection],
  );

  useEffect(() => {
    const savedPlayerId = Number(sessionStorage.getItem("uno.playerId"));
    const savedToken = sessionStorage.getItem("uno.reconnectToken");

    if (!savedPlayerId || !savedToken || connection || game) return undefined;

    setRejoiningGame(true);
    let cancelled = false;
    const hub = openHub({
      playerId: savedPlayerId,
      reconnectToken: savedToken,
      onLobby: (state) => {
        setLobby(state);
        setFriendsRole(state?.hostPlayerId === savedPlayerId ? "host" : "join");
      },
      onGame: (state) => {
        if (state) {
          setGame(state);
          setRejoiningGame(false);
        }
      },
      onError: (message) => setError?.(message),
      onStatus: setConnectionStatus,
      onTerminal: (message) => {
        setGame(null);
        setTerminalMessage(message);
        setConnectionStatus("forfeited");
      },
    });

    (async () => {
      try {
        await hub.start();
        const state = await hub.invoke("Register", savedPlayerId, savedToken);
        if (!cancelled) {
          setPlayerId(savedPlayerId);
          setConnection(hub);
          setConnectionStatus("connected");
          const gameState =
            state ||
            (await request(
              `/lobby/game/${savedPlayerId}?reconnectToken=${encodeURIComponent(savedToken)}`,
            ));
          if (gameState) setGame(gameState);
          setRejoiningGame(false);
        }
      } catch (error) {
        if (!cancelled) {
          setRejoiningGame(false);
          const terminalState = terminalConnectionError(error.message);
          if (terminalState) {
            setGame(null);
            setTerminalMessage(terminalState.message);
            setConnectionStatus(terminalState.status);
            hub.stop();
          } else {
            setError?.(error.message);
            setConnectionStatus("disconnected");
          }
        }
      }
    })();

    return () => {
      cancelled = true;
      hub.stop();
    };
  // Reconnection must not be torn down when the restored game snapshot arrives.
  // `game` and `connection` changes are expected during this flow; only unmount
  // should stop this hub.
  }, [setError]);

  useEffect(() => {
    const savedName = localStorage.getItem("uno.playerName")?.trim();
    if (
      !savedName ||
      sessionStorage.getItem("uno.playerId") ||
      sessionStorage.getItem("uno.reconnectToken") ||
      connection ||
      game
    )
      return undefined;

    let cancelled = false;
    let hub;
    (async () => {
      try {
        const lobbyState = await request("/lobby");
        if (!lobbyState.isPlaying || cancelled) return;

        const joined = await request("/lobby/join", "POST", {
          playerName: savedName,
          opponentCount: 1,
          hostLobby: false,
        });

        if (cancelled) return;
        sessionStorage.setItem("uno.playerId", String(joined.playerId));
        sessionStorage.setItem("uno.reconnectToken", joined.reconnectToken);
        setName(savedName);
        setLobby(joined.lobby);
        setFriendsRole(joined.isHost ? "host" : "join");
        setRejoiningGame(true);

        hub = openHub({
          playerId: joined.playerId,
          reconnectToken: joined.reconnectToken,
          onLobby: setLobby,
          onGame: (state) => {
            if (state) {
              setGame(state);
              setRejoiningGame(false);
            } else {
              setGame(null);
              setRejoiningGame(false);
            }
          },
          onError: (message) => setError?.(message),
          onStatus: setConnectionStatus,
          onTerminal: (message) => {
            setTerminalMessage(message);
            hub?.stop();
          },
        });

        await hub.start();
        const state = await hub.invoke(
          "Register",
          joined.playerId,
          joined.reconnectToken,
        );
        if (!cancelled) {
          setPlayerId(joined.playerId);
          setConnection(hub);
          setConnectionStatus("connected");
          if (state || joined.game) setGame(state || joined.game);
          setRejoiningGame(false);
        }
      } catch (error) {
        if (!cancelled) {
          setRejoiningGame(false);
          setError?.(error.message);
          hub?.stop();
        }
      }
    })();

    return () => {
      cancelled = true;
      hub?.stop();
    };
  // Keep the reconnect hub alive while its game/lobby state is being restored.
  }, [setError]);

  return {
    game,
    setGame,
    lobby,
    playerId,
    connection,
    connectionStatus,
    terminalMessage,
    rejoiningGame,
    friendsRole,
    setFriendsRole,
    name,
    setName,
    opponents,
    setOpponents,
    hostOrJoin,
    leaveLobby,
    returnToLobby,
  };
}
