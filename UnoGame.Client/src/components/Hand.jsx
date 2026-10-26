import CardFace from './CardFace.jsx';

export default function Hand({
  game,
  human,
  displayedHand,
  busy,
  error,
  activePlay,
  animationQueue,
  isYourTurn,
  draw,
  challengeDrawFour,
  pass,
  sayUno,
  setWildCard,
  play,
}) {
  return (
    <section className="hand-area">
      <div className="hand-heading">
        <div>
          <p className="eyebrow">YOUR HAND</p>
          <h2>{human.name}<span>{displayedHand.length} cards</span></h2>
        </div>
        <div className="hand-actions">{game.canChallengeDrawFour && <><button className="subtle-button pass-button" onClick={challengeDrawFour} disabled={busy || Boolean(activePlay) || animationQueue.length > 0}>Challenge +4</button><button className="subtle-button pass-button" onClick={draw} disabled={busy || Boolean(activePlay) || animationQueue.length > 0}>Draw 4</button></>}{isYourTurn && game.hasDrawnThisTurn && <button className="subtle-button pass-button" onClick={pass} disabled={busy || Boolean(activePlay) || animationQueue.length > 0}>Pass</button>}<button className="subtle-button uno-button" onClick={sayUno} disabled={!isYourTurn || busy || Boolean(activePlay) || animationQueue.length > 0 || displayedHand.length !== 2}>✳ &nbsp;Call UNO</button></div>
      </div>
      <div className="hand-cards">{displayedHand.map(card => <CardFace key={card.id} card={card} disabled={!isYourTurn || busy || Boolean(activePlay) || animationQueue.length > 0} onClick={() => card.color === 'Wild' ? setWildCard(card) : play(card)} />)}</div>
      {error && <p className="error-message game-error">{error}</p>}
    </section>
  );
}
