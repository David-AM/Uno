import { useEffect, useState } from 'react';

const API = import.meta.env.VITE_API_URL || 'http://localhost:5124/api';
const colors = ['Red', 'Blue', 'Green', 'Yellow'];
const colorsClass = { Red: 'red', Blue: 'blue', Green: 'green', Yellow: 'yellow', Wild: 'wild' };

async function request(path, method = 'GET', body) {
  const response = await fetch(`${API}${path}`, {
    method,
    headers: body ? { 'Content-Type': 'application/json' } : undefined,
    body: body ? JSON.stringify(body) : undefined,
  });
  const result = await response.json().catch(() => ({}));
  if (!response.ok) throw new Error(result.error || 'The request could not be completed.');
  return result;
}

function CardFace({ card, small = false, onClick, disabled }) {
  const label = card.action ? card.action.replace('WildDrawFour', '+4').replace('DrawTwo', '+2') : card.value;
  const symbolAction = card.action === 'Skip' || card.action === 'Reverse';
  return <button type="button" disabled={disabled} onClick={onClick} className={`playing-card ${colorsClass[card.color]} ${symbolAction ? 'symbol-action' : ''} ${small ? 'small' : ''} ${disabled ? 'disabled' : ''}`} aria-label={`${card.color} ${card.action || card.value}`}>
    <span className="card-corner">{label}</span><span className="card-center">{label}</span><span className="card-corner bottom">{label}</span>
  </button>;
}

function App() {
  const [game, setGame] = useState(null);
  const [name, setName] = useState('');
  const [opponents, setOpponents] = useState(3);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const [wildCard, setWildCard] = useState(null);
  const [animationQueue, setAnimationQueue] = useState([]);
  const [activePlay, setActivePlay] = useState(null);
  const isYourTurn = Boolean(game?.players.find(p => p.isCurrentPlayer)?.isHuman && game.status === 'playing');

  const act = async (operation) => {
    if (busy || activePlay || animationQueue.length) return;
    setBusy(true); setError('');
    try { setGame(await operation()); }
    catch (e) { setError(e.message); }
    finally { setBusy(false); }
  };
  const createGame = () => act(async () => request('/games', 'POST', { playerName: name.trim() || 'You', opponentCount: opponents }));
  const play = (card, chosenColor = null) => act(async () => request(`/games/${game.gameId}/play`, 'POST', { cardId: card.id, chosenColor }));
  const draw = () => act(async () => request(`/games/${game.gameId}/draw`, 'POST'));
  const uno = () => act(async () => request(`/games/${game.gameId}/uno`, 'POST'));
  const catchUno = (playerId) => act(async () => request(`/games/${game.gameId}/catch-uno`, 'POST', { playerId }));
  const restart = () => act(async () => request(`/games/${game.gameId}/restart`, 'POST'));

  useEffect(() => {
    if (!game?.gameId || game.status !== 'playing') return undefined;
    const timer = window.setInterval(async () => {
      try { const updated = await request(`/games/${game.gameId}`); setGame(updated); }
      catch { /* Keep the last rendered game if a refresh briefly fails. */ }
    }, 5000);
    return () => window.clearInterval(timer);
  }, [game?.gameId, game?.status]);

  useEffect(() => {
    if (!game?.recentPlays?.length) return;
    setAnimationQueue(queue => [...queue, ...game.recentPlays.map((play, index) => ({ ...play, animationId: `${Date.now()}-${index}-${Math.random()}` }))]);
  }, [game?.recentPlays]);

  useEffect(() => {
    const currentPlay = animationQueue[0];
    if (!currentPlay) { setActivePlay(null); return undefined; }
    setActivePlay(currentPlay);
    const timer = window.setTimeout(() => setAnimationQueue(queue => queue.slice(1)), 800);
    return () => window.clearTimeout(timer);
  }, [animationQueue[0]?.animationId]);

  useEffect(() => {
    if (!game?.gameId || !game.hasDrawnThisTurn || !isYourTurn || busy || activePlay || animationQueue.length) return undefined;
    const gameId = game.gameId;
    const timer = window.setTimeout(() => { setWildCard(null); act(async () => request(`/games/${gameId}/pass`, 'POST')); }, 2000);
    return () => window.clearTimeout(timer);
  }, [game?.gameId, game?.hasDrawnThisTurn, isYourTurn, busy, activePlay, animationQueue.length]);

  if (!game) return <main className="landing">
    <div className="landing-decoration decoration-one" /><div className="landing-decoration decoration-two" />
    <header className="brand"><span className="brand-mark">U</span><span>TABLE <b>CLUB</b></span></header>
    <section className="welcome-card"><p className="eyebrow">A LITTLE FRIENDLY COMPETITION</p><h1>One more<br /><em>round?</em></h1><p className="intro">Gather your colors, watch the table, and be the first to play your last card.</p>
      <form onSubmit={e => { e.preventDefault(); createGame(); }}>
        <label htmlFor="player-name">YOUR NAME</label><input id="player-name" value={name} onChange={e => setName(e.target.value)} placeholder="Enter your name" maxLength="24" />
        <label htmlFor="opponents">OPPONENTS</label><select id="opponents" value={opponents} onChange={e => setOpponents(Number(e.target.value))}><option value="1">1 opponent</option><option value="2">2 opponents</option><option value="3">3 opponents</option></select>
        {error && <p className="error-message">{error}</p>}<button className="primary-button" disabled={busy}>{busy ? 'Setting the table…' : 'Start a game'} <span>↗</span></button>
      </form><p className="landing-note">Classic rules · You vs. the table</p>
    </section><footer>TAKE YOUR TIME. PICK YOUR MOMENT.</footer>
  </main>;

  const human = game.players.find(p => p.isHuman);
  const opponentsList = game.players.filter(p => !p.isHuman);
  const canDraw = isYourTurn;
  const animatedSeat = activePlay && game.players.findIndex(player => player.id === activePlay.playerId);
  return <main className="game-shell">
    <header className="game-header"><a className="brand" href="#" onClick={e => { e.preventDefault(); setGame(null); }}><span className="brand-mark">U</span><span>TABLE <b>CLUB</b></span></a><div className="round-label"><span className="live-dot" /> FRIENDLY GAME <span className="round-divider">/</span> ROUND 01</div><button className="icon-button" title="Start a new round" onClick={restart} disabled={busy}>↻ <span>New round</span></button></header>
    <section className="table-area">
      <div className="opponent-row">{opponentsList.map((player, index) => <div className={`opponent ${player.isCurrentPlayer ? 'active' : ''}`} key={player.id}>
        <div className={`avatar avatar-${index}`}>{player.name.slice(0, 1).toUpperCase()}</div><div><strong>{player.name}</strong><span>{player.cardCount} {player.cardCount === 1 ? 'card' : 'cards'}</span></div>{player.isCurrentPlayer && <span className="turn-badge">THINKING</span>}{player.cardCount === 1 && <button className="catch-button" disabled={busy || game.status !== 'playing'} onClick={() => catchUno(player.id)}>CATCH UNO</button>}
      </div>)}</div>
      <div className="felt"><div className="table-stitch" /><div className="table-topline"><span>THE TABLE</span><span className="direction">{game.direction === 'Clockwise' ? '↻' : '↺'} &nbsp;{game.direction}</span></div>
        <div className="pile-row"><div className="pile-wrap"><div className="pile-count">{game.drawPileCount}</div><button className="draw-pile" onClick={draw} disabled={!canDraw || busy} aria-label="Draw a card"><span>U</span><i>UNO</i></button><span className="pile-caption">DRAW PILE</span></div>
          <div className="turn-arrow">→</div><div className="discard-wrap">{game.topCard && <CardFace card={game.topCard} />}<span className="pile-caption">DISCARD PILE</span></div></div>
        <div className="active-color"><span>IN PLAY</span><i className={`color-dot ${colorsClass[game.activeColor]}`} />{game.activeColor}</div>
      </div>
      <div className="status-line" role="status" aria-live="polite"><div className="status-icon">✳</div><span>{game.message || (isYourTurn ? 'Your turn. Find a match.' : 'The table is playing…')}</span>{isYourTurn && <b>YOUR TURN</b>}</div>
    </section>
    <section className="hand-area"><div className="hand-heading"><div><p className="eyebrow">YOUR HAND</p><h2>{human.name}<span>{game.hand.length} cards</span></h2></div><div className="hand-actions"><button className="subtle-button" onClick={uno} disabled={!isYourTurn || busy || game.hand.length !== 1}>✳ &nbsp;Call UNO</button></div></div>
      <div className="hand-cards">{game.hand.map(card => <CardFace key={card.id} card={card} disabled={!isYourTurn || busy || Boolean(activePlay) || animationQueue.length > 0} onClick={() => card.color === 'Wild' ? setWildCard(card) : play(card)} />)}</div>
      {error && <p className="error-message game-error">{error}</p>}
    </section>
    <footer className="game-footer"><span>PLAY A MATCHING COLOR OR SYMBOL</span><span>DRAWN CARDS PASS AFTER 2 SECONDS</span></footer>
    {game.alertMessage && <div className="floating-alert" role="alert" aria-live="assertive"><span>⚠</span>{game.alertMessage}</div>}
    {activePlay && <div className={`play-flight from-seat-${animatedSeat}`} key={activePlay.animationId} aria-hidden="true"><span>{activePlay.playerName}</span><CardFace card={activePlay.card} disabled /></div>}
    {wildCard && <div className="modal-backdrop" role="presentation" onClick={() => setWildCard(null)}><section className="color-modal" role="dialog" aria-modal="true" aria-labelledby="color-title" onClick={e => e.stopPropagation()}><button className="close-modal" onClick={() => setWildCard(null)}>×</button><p className="eyebrow">A WILD MOVE</p><h2 id="color-title">Choose a color</h2><p>Set the color that continues the round.</p><div className="color-options">{colors.map(color => <button key={color} className={`color-choice ${colorsClass[color]}`} onClick={() => { const selected = wildCard; setWildCard(null); play(selected, color); }}><span />{color}</button>)}</div></section></div>}
    {game.status === 'completed' && <div className="modal-backdrop"><section className="color-modal result-modal"><div className="result-star">✳</div><p className="eyebrow">ROUND COMPLETE</p><h2>{game.winner === human.name ? 'You did it!' : `${game.winner} wins!`}</h2><p>{game.winner === human.name ? 'A very well played round.' : 'There is always another round.'}</p><button className="primary-button" onClick={restart}>Play again <span>↻</span></button></section></div>}
  </main>;
}

export default App;
