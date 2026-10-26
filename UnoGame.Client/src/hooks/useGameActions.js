import { useEffect, useState } from "react";
import Swal from "sweetalert2";

export function useGameActions({
  game,
  playerId,
  connection,
  setGame,
  activePlay,
  animationQueue,
  blocked,
  error,
  setError,
}) {
  const [busy, setBusy] = useState(false);
  const [wildCard, setWildCard] = useState(null);

  const isYourTurn = Boolean(
    game?.players.find((p) => p.isCurrentPlayer)?.isHuman &&
    game.status === "playing" &&
    !game.isPaused,
  );
  const matchPaused = Boolean(game?.isPaused);

  const act = async (operation) => {
    if (busy || blocked || matchPaused) return;
    setBusy(true);
    setError("");
    try {
      const result = await operation();
      if (result) setGame(result);
    } catch (e) {
      setError(e.message);
    } finally {
      setBusy(false);
    }
  };

  const multiplayerAction = (method, ...args) =>
    act(async () => {
      if (!connection) throw new Error("Connecting to the lobby…");
      await connection.invoke(method, playerId, ...args);
    });

  const play = (card, chosenColor = null) =>
    multiplayerAction("Play", card.id, chosenColor);
  const draw = () => multiplayerAction("Draw");
  const pass = () => multiplayerAction("Pass");
  const sayUno = () =>
    act(async () => {
      await connection.invoke("SayUno", playerId);
      return null;
    });
  const challengeDrawFour = () => multiplayerAction("ChallengeDrawFour");
  const chooseOpeningColor = (color) =>
    multiplayerAction("ChooseOpeningColor", color);
  const catchUno = (targetPlayerId) =>
    multiplayerAction("CatchUno", targetPlayerId);
  const startGame = () => multiplayerAction("Start");
  const restart = (resetScores = false) =>
    multiplayerAction("Restart", resetScores);

  const restartGame = async () => {
    const result = await Swal.fire({
      title: "Restart game?",
      text: "Your current round and scores will be reset.",
      icon: "warning",
      showCancelButton: true,
      confirmButtonText: "Restart",
      cancelButtonText: "Cancel",
      reverseButtons: true,
      customClass: {
        popup: "uno-swal-popup",
        title: "uno-swal-title",
        htmlContainer: "uno-swal-text",
      },
    });
    if (result.isConfirmed) await restart(true);
  };

  useEffect(() => {
    if (
      !game?.gameId ||
      !game.hasDrawnThisTurn ||
      !isYourTurn ||
      busy ||
      activePlay ||
      animationQueue.length
    )
      return undefined;
    const timer = window.setTimeout(() => {
      setWildCard(null);
      pass();
    }, 4000);
    return () => window.clearTimeout(timer);
  }, [
    game?.gameId,
    game?.hasDrawnThisTurn,
    isYourTurn,
    busy,
    activePlay,
    animationQueue.length,
    pass,
  ]);

  return {
    busy,
    error,
    setError,
    wildCard,
    setWildCard,
    play,
    draw,
    pass,
    sayUno,
    catchUno,
    challengeDrawFour,
    chooseOpeningColor,
    startGame,
    restart,
    restartGame,
    isYourTurn,
    matchPaused,
  };
}
