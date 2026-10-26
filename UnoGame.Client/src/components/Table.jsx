import CardFace from "./CardFace.jsx";

const colors = ["Red", "Blue", "Green", "Yellow"];
const colorsClass = {
  Red: "red",
  Blue: "blue",
  Green: "green",
  Yellow: "yellow",
  Wild: "wild",
};

export default function Table({
  game,
  connectionStatus,
  matchPaused,
  human,
  eligiblePlayers,
  nextPlayer,
  activePlay,
  busy,
  animationQueue,
  displayedTopCard,
  draw,
  catchUno,
  setWildCard,
  play,
  chooseOpeningColor,
  wildCard,
  restartGame,
}) {
  const isYourTurn = Boolean(
    game?.players.find((p) => p.isCurrentPlayer)?.isHuman &&
    game.status === "playing" &&
    !game.isPaused,
  );
  const humanIndex = eligiblePlayers.findIndex((player) => player.isHuman);
  const opponentsList =
    humanIndex >= 0
      ? [
          ...eligiblePlayers.slice(humanIndex + 1),
          ...eligiblePlayers.slice(0, humanIndex),
        ]
      : [];
  const canDraw = isYourTurn && !game.canChallengeDrawFour && !matchPaused;
  const directionStep = game.direction === "Clockwise" ? 1 : -1;
  const currentPlayerIndex = eligiblePlayers.findIndex(
    (player) => player.isCurrentPlayer,
  );
  const nextPlayerName =
    currentPlayerIndex >= 0 && eligiblePlayers.length > 1
      ? eligiblePlayers[
          (currentPlayerIndex + directionStep + eligiblePlayers.length) %
            eligiblePlayers.length
        ]
      : null;
  const animatedSeat =
    activePlay && activePlay.playerId === game.humanPlayerId
      ? 0
      : activePlay &&
        opponentsList.findIndex((player) => player.id === activePlay.playerId) +
          1;
  const inPlayLabel = displayedTopCard?.action
    ? displayedTopCard.action
        .replace("WildDrawFour", "+4")
        .replace("DrawTwo", "+2")
    : displayedTopCard?.value;
  const inPlayColor =
    displayedTopCard?.color === "Wild"
      ? game.activeColor
      : displayedTopCard?.color || game.activeColor;

  return (
    <>
      {connectionStatus !== "connected" && (
        <div className="error-message game-error">
          Connection {connectionStatus}. Reconnecting automatically…
        </div>
      )}
      {matchPaused && (
        <div className="error-message game-error">
          {game.disconnectedPlayer || "A player"} disconnected. Waiting for
          reconnection
          {game.reconnectSecondsRemaining != null
            ? ` · ${game.reconnectSecondsRemaining}s remaining`
            : "…"}
        </div>
      )}
      <header className="game-header">
        <a
          className="brand"
          href="#"
          onClick={(e) => {
            e.preventDefault();
            setGame(null);
          }}
        >
          <span className="brand-mark">U</span>
          <span>
            UNO <b>CIHUY</b>
          </span>
        </a>
        <div className="round-label">
          <span className="live-dot" /> FRIENDLY GAME{" "}
          <span className="round-divider">/</span> ROUND{" "}
          {String(game.roundNumber).padStart(2, "0")}
        </div>
        <div className="score-display">
          SCORE <b>{human.score}</b>
        </div>
        <button
          className="icon-button"
          title="Restart game"
          onClick={restartGame}
          disabled={busy || Boolean(activePlay) || animationQueue.length > 0}
        >
          ↻ <span>Restart</span>
        </button>
      </header>
      <section className="table-area">
        <div className="opponent-row">
          {opponentsList.map((player, index) => (
            <div
              className={`opponent ${player.isCurrentPlayer ? "active" : ""}`}
              key={player.id}
            >
              <div className={`avatar avatar-${index}`}>
                {player.name.slice(0, 1).toUpperCase()}
              </div>
              <div>
                <strong>{player.name}</strong>
                <span>
                  {player.cardCount} {player.cardCount === 1 ? "card" : "cards"}{" "}
                  · {player.score} pts
                </span>
              </div>
              {player.isCurrentPlayer && (
                <span className="turn-badge">THINKING</span>
              )}
              {player.cardCount === 1 && (
                <button
                  className="catch-button"
                  disabled={
                    busy ||
                    Boolean(activePlay) ||
                    animationQueue.length > 0 ||
                    game.status !== "playing"
                  }
                  onClick={() => catchUno(player.id)}
                  aria-label={`Catch ${player.name} for not calling UNO`}
                >
                  CATCH UNO · {player.name}
                </button>
              )}
            </div>
          ))}
        </div>
        <span className="next-player">
          NEXT: <b>{nextPlayer?.name || "—"}</b>
        </span>
        <div className="felt">
          <div className="table-stitch" />
          <div className="table-topline">
            <span>THE TABLE</span>
            <span className="direction">
              {game.direction === "Clockwise" ? "↻" : "↺"} &nbsp;
              {game.direction}
            </span>
          </div>
          <div className="pile-row">
            <div className="pile-wrap">
              <div className="pile-count">{game.drawPileCount}</div>
              <button
                className="draw-pile"
                onClick={draw}
                disabled={
                  !canDraw ||
                  busy ||
                  Boolean(activePlay) ||
                  animationQueue.length > 0
                }
                aria-label="Draw a card"
              >
                <span>U</span>
                <i>UNO</i>
              </button>
              <span className="pile-caption">DRAW PILE</span>
            </div>
            <div className="turn-arrow">→</div>
            <div className="discard-wrap">
              {displayedTopCard && <CardFace card={displayedTopCard} />}
              <span className="pile-caption">DISCARD PILE</span>
            </div>
          </div>
          <div className="active-color">
            <span>IN PLAY</span>
            <i className={`color-dot ${colorsClass[inPlayColor]}`} />
            {inPlayColor}
            {inPlayLabel !== undefined && <b> · {inPlayLabel}</b>}
          </div>
        </div>
      </section>
      {activePlay?.type === "draw" && (
        <div
          className={`draw-flight from-seat-${animatedSeat}`}
          key={activePlay.animationId}
          aria-hidden="true"
        >
          <span>{activePlay.playerName}</span>
          <div className="draw-pile draw-pile-animation">
            <span>U</span>
            <i>UNO</i>
          </div>
        </div>
      )}
      {activePlay?.type === "play" && (
        <div
          className={`play-flight from-seat-${animatedSeat}`}
          key={activePlay.animationId}
          aria-hidden="true"
        >
          <span>{activePlay.playerName}</span>
          <CardFace card={activePlay.card} disabled />
        </div>
      )}
      {wildCard && (
        <div
          className="modal-backdrop"
          role="presentation"
          onClick={() => setWildCard(null)}
        >
          <section
            className="color-modal"
            role="dialog"
            aria-modal="true"
            aria-labelledby="color-title"
            onClick={(e) => e.stopPropagation()}
          >
            <button className="close-modal" onClick={() => setWildCard(null)}>
              ×
            </button>
            <p className="eyebrow">A WILD MOVE</p>
            <h2 id="color-title">Choose a color</h2>
            <p>Set the color that continues the round.</p>
            <div className="color-options">
              {colors.map((color) => (
                <button
                  key={color}
                  className={`color-choice ${colorsClass[color]}`}
                  onClick={() => {
                    const selected = wildCard;
                    setWildCard(null);
                    play(selected, color);
                  }}
                >
                  <span />
                  {color}
                </button>
              ))}
            </div>
          </section>
        </div>
      )}
      {game.requiresOpeningColorChoice && (
        <div className="modal-backdrop">
          <section
            className="color-modal"
            role="dialog"
            aria-modal="true"
            aria-labelledby="opening-color-title"
          >
            <p className="eyebrow">OPENING WILD</p>
            <h2 id="opening-color-title">Choose a color</h2>
            <p>Choose the color before making the first play.</p>
            <div className="color-options">
              {colors.map((color) => (
                <button
                  key={color}
                  className={`color-choice ${colorsClass[color]}`}
                  onClick={() => chooseOpeningColor(color)}
                  disabled={busy}
                >
                  <span />
                  {color}
                </button>
              ))}
            </div>
          </section>
        </div>
      )}
    </>
  );
}
