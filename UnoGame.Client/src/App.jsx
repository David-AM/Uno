import { useEffect, useState } from 'react';
import { HubConnectionBuilder, LogLevel } from '@microsoft/signalr';
import Swal from 'sweetalert2';
import 'sweetalert2/dist/sweetalert2.min.css';

const API = import.meta.env.VITE_API_URL || `${window.location.protocol}//${window.location.hostname}:5124/api`;
const HUB = `${API.replace(/\/api\/?$/, '')}/hubs/multiplayer`;
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
  const [lobby, setLobby] = useState(null);
  const [playerId, setPlayerId] = useState(null);
  const [connection, setConnection] = useState(null);
  const [connectionStatus, setConnectionStatus] = useState('offline');
  const [terminalMessage, setTerminalMessage] = useState('');
  const [rejoiningGame, setRejoiningGame] = useState(false);
  const [mode, setMode] = useState(null);
  const [friendsRole, setFriendsRole] = useState(null);
  const [name, setName] = useState('');
  const [opponents, setOpponents] = useState(3);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const [wildCard, setWildCard] = useState(null);
  const [animationQueue, setAnimationQueue] = useState([]);
  const [activePlay, setActivePlay] = useState(null);
  const [displayedTopCard, setDisplayedTopCard] = useState(null);
  const [displayedHand, setDisplayedHand] = useState([]);
  const hasName = name.trim().length > 0;
  const isYourTurn = Boolean(game?.players.find(p => p.isCurrentPlayer)?.isHuman && game.status === 'playing' && !game.isPaused);
  const matchPaused = Boolean(game?.isPaused);
  const terminalConnectionError = message => /forfeit|reconnection window/i.test(message || '')
    ? { status: 'forfeited', message: 'You missed the 60-second reconnection window. You have forfeited this match.' }
    : /match has ended|no longer available/i.test(message || '')
      ? { status: 'match-ended', message: 'This match has ended.' }
      : null;
  const returnToLobby = () => {
    connection?.stop();
    sessionStorage.removeItem('uno.playerId');
    sessionStorage.removeItem('uno.reconnectToken');
    localStorage.removeItem('uno.playerName');
    setConnection(null); setPlayerId(null); setLobby(null); setGame(null);
    setMode(null); setFriendsRole(null); setTerminalMessage(''); setConnectionStatus('offline'); setError('');
  };
  const leaveLobby = async () => {
    if (!connection || !playerId) returnToLobby();
    else {
      setBusy(true);
      try { await connection.invoke('LeaveLobby', playerId); returnToLobby(); }
      catch (e) { setError(e.message); }
      finally { setBusy(false); }
    }
  };

  const act = async (operation) => {
    if (busy || activePlay || animationQueue.length || matchPaused) return;
    setBusy(true); setError('');
    try { const result = await operation(); if (result) setGame(result); }
    catch (e) { setError(e.message); }
    finally { setBusy(false); }
  };
  const createGame = () => act(async () => {
    if (!name.trim()) throw new Error('Enter your name before starting or hosting a game.');
    setTerminalMessage('');
    if (mode === 'bot') {
      return request('/games', 'POST', { playerName: name.trim() || 'You', opponentCount: opponents });
    }
    const joined = await request('/lobby/join', 'POST', {
      playerName: name.trim() || 'You',
      opponentCount: opponents,
      hostLobby: friendsRole === 'host',
    });
    setPlayerId(joined.playerId); setLobby(joined.lobby);
    sessionStorage.setItem('uno.playerId', String(joined.playerId));
    sessionStorage.setItem('uno.reconnectToken', joined.reconnectToken);
    localStorage.setItem('uno.playerName', name.trim());
    const hub = new HubConnectionBuilder().withUrl(HUB).withAutomaticReconnect([0, 2000, 10000, 30000]).configureLogging(LogLevel.Warning).build();
    hub.on('lobbyUpdated', setLobby);
    hub.on('gameUpdated', state => { if (state) setGame(state); });
    hub.on('actionError', setError);
    let terminal = false;
    hub.onreconnecting(() => { if (!terminal) setConnectionStatus('reconnecting'); });
    hub.onreconnected(async () => {
      try { const state = await hub.invoke('Register', joined.playerId, joined.reconnectToken); if (state) setGame(state); setConnectionStatus('connected'); }
      catch (e) {
        const terminalState = terminalConnectionError(e.message);
        if (terminalState) { terminal = true; setGame(null); setTerminalMessage(terminalState.message); setConnectionStatus(terminalState.status); hub.stop(); }
        else { setError(e.message); setConnectionStatus('disconnected'); }
      }
    });
    hub.onclose(() => { if (!terminal) setConnectionStatus('disconnected'); });
    try {
      await hub.start();
      const current = await hub.invoke('Register', joined.playerId, joined.reconnectToken);
      setConnectionStatus('connected'); setConnection(hub);
      return current || joined.game;
    } catch (e) {
      const terminalState = terminalConnectionError(e.message);
      if (terminalState) { terminal = true; setGame(null); setTerminalMessage(terminalState.message); setConnectionStatus(terminalState.status); hub.stop(); return null; }
      throw e;
    }
  });
  const multiplayerAction = (method, ...args) => act(async () => { if (!connection) throw new Error('Connecting to the lobby…'); await connection.invoke(method, playerId, ...args); });
  const botAction = (path, method = 'POST', body) => act(() => request(`/games/${game.gameId}${path}`, method, body));
  const play = (card, chosenColor = null) => mode === 'bot'
    ? botAction('/play', 'POST', { cardId: card.id, chosenColor })
    : multiplayerAction('Play', card.id, chosenColor);
  const draw = () => mode === 'bot' ? botAction('/draw') : multiplayerAction('Draw');
  const pass = () => mode === 'bot' ? botAction('/pass') : multiplayerAction('Pass');
  const uno = () => act(async () => {
    if (mode === 'bot') await request(`/games/${game.gameId}/uno`, 'POST');
    else await connection.invoke('SayUno', playerId);
    if (mode === 'bot') Swal.fire({ toast: true, position: 'top', icon: 'success', title: 'UNO called!', showConfirmButton: false, timer: 1600, timerProgressBar: true, customClass: { popup: 'uno-swal-popup', title: 'uno-swal-title' } });
    return null;
  });
  const challengeDrawFour = () => mode === 'bot'
    ? botAction('/challenge-draw-four')
    : multiplayerAction('ChallengeDrawFour');
  const chooseOpeningColor = color => mode === 'bot'
    ? botAction('/opening-color', 'POST', { color })
    : multiplayerAction('ChooseOpeningColor', color);
  const catchUno = targetPlayerId => mode === 'bot'
    ? botAction('/catch-uno', 'POST', { playerId: targetPlayerId })
    : multiplayerAction('CatchUno', targetPlayerId);
  const startGame = () => multiplayerAction('Start');
  const restart = (resetScores = false) => mode === 'bot'
    ? botAction(`/restart?resetScores=${resetScores}`)
    : multiplayerAction('Restart', resetScores);
  const restartGame = async () => {
    const result = await Swal.fire({
      title: 'Restart game?',
      text: 'Your current round and scores will be reset.',
      icon: 'warning',
      showCancelButton: true,
      confirmButtonText: 'Restart',
      cancelButtonText: 'Cancel',
      reverseButtons: true,
      customClass: {
        popup: 'uno-swal-popup',
        title: 'uno-swal-title',
        htmlContainer: 'uno-swal-text',
      },
    });
    if (result.isConfirmed) await restart(true);
  };

  useEffect(() => () => { connection?.stop(); }, [connection]);

  useEffect(() => {
    const savedPlayerId = Number(sessionStorage.getItem('uno.playerId'));
    const savedToken = sessionStorage.getItem('uno.reconnectToken');
    if (!savedPlayerId || !savedToken || connection || game) return undefined;
    setRejoiningGame(true);
    let cancelled = false;
    const hub = new HubConnectionBuilder().withUrl(HUB).withAutomaticReconnect([0, 2000, 10000, 30000]).configureLogging(LogLevel.Warning).build();
    hub.on('lobbyUpdated', state => { setLobby(state); setMode('friends'); setFriendsRole(state?.hostPlayerId === savedPlayerId ? 'host' : 'join'); });
    hub.on('gameUpdated', state => { if (state) { setGame(state); setRejoiningGame(false); } });
    hub.on('actionError', setError);
    let terminal = false;
    hub.onreconnecting(() => { if (!terminal) setConnectionStatus('reconnecting'); });
    hub.onreconnected(async () => {
      try { const state = await hub.invoke('Register', savedPlayerId, savedToken); if (state) setGame(state); setConnectionStatus('connected'); }
      catch (e) {
        const terminalState = terminalConnectionError(e.message);
        if (terminalState) { terminal = true; setGame(null); setTerminalMessage(terminalState.message); setConnectionStatus(terminalState.status); hub.stop(); }
        else { setError(e.message); setConnectionStatus('disconnected'); }
      }
    });
    hub.onclose(() => { if (!terminal) setConnectionStatus('disconnected'); });
    (async () => {
      try {
        await hub.start();
        const state = await hub.invoke('Register', savedPlayerId, savedToken);
        if (!cancelled) {
          setPlayerId(savedPlayerId); setConnection(hub); setConnectionStatus('connected');
          const gameState = state || await request(`/lobby/game/${savedPlayerId}?reconnectToken=${encodeURIComponent(savedToken)}`);
          if (gameState) setGame(gameState);
          setRejoiningGame(false);
        }
      } catch (e) {
        if (!cancelled) {
          setRejoiningGame(false);
          const terminalState = terminalConnectionError(e.message);
          if (terminalState) { terminal = true; setGame(null); setTerminalMessage(terminalState.message); setConnectionStatus(terminalState.status); hub.stop(); }
          else { setError(e.message); setConnectionStatus('disconnected'); }
        }
      }
    })();
    return () => { cancelled = true; hub.stop(); };
  }, []);

  // A closed tab loses sessionStorage. Recover the player by name when the
  // server still has an active game, then receive a fresh session token.
  useEffect(() => {
    const savedName = localStorage.getItem('uno.playerName')?.trim();
    if (!savedName || sessionStorage.getItem('uno.playerId') || sessionStorage.getItem('uno.reconnectToken') || connection || game) return undefined;
    let cancelled = false;
    let hub;
    (async () => {
      try {
        const lobbyState = await request('/lobby');
        if (!lobbyState.isPlaying || cancelled) return;
        const joined = await request('/lobby/join', 'POST', {
          playerName: savedName,
          opponentCount: 1,
          hostLobby: false,
        });
        if (cancelled) return;
        sessionStorage.setItem('uno.playerId', String(joined.playerId));
        sessionStorage.setItem('uno.reconnectToken', joined.reconnectToken);
        setName(savedName); setLobby(joined.lobby); setMode('friends'); setFriendsRole(joined.isHost ? 'host' : 'join'); setRejoiningGame(true);
        hub = new HubConnectionBuilder().withUrl(HUB).withAutomaticReconnect([0, 2000, 10000, 30000]).configureLogging(LogLevel.Warning).build();
        hub.on('lobbyUpdated', setLobby);
        hub.on('gameUpdated', state => { if (state) { setGame(state); setRejoiningGame(false); } });
        hub.on('actionError', setError);
        let terminal = false;
        hub.onreconnecting(() => { if (!terminal) setConnectionStatus('reconnecting'); });
        hub.onreconnected(async () => {
          try { const state = await hub.invoke('Register', joined.playerId, joined.reconnectToken); if (state) setGame(state); setConnectionStatus('connected'); }
          catch (e) { const terminalState = terminalConnectionError(e.message); if (terminalState) { terminal = true; setTerminalMessage(terminalState.message); hub.stop(); } else setError(e.message); }
        });
        hub.onclose(() => { if (!terminal) setConnectionStatus('disconnected'); });
        await hub.start();
        const state = await hub.invoke('Register', joined.playerId, joined.reconnectToken);
        if (!cancelled) { setPlayerId(joined.playerId); setConnection(hub); setConnectionStatus('connected'); if (state || joined.game) setGame(state || joined.game); setRejoiningGame(false); }
      } catch (e) {
        if (!cancelled) { setRejoiningGame(false); setError(e.message); hub?.stop(); }
      }
    })();
    return () => { cancelled = true; hub?.stop(); };
  }, []);

  useEffect(() => {
    if (!game?.recentEvents?.length) return;
    setAnimationQueue(queue => [...queue, ...game.recentEvents.map((event, index) => ({ ...event, animationId: `${Date.now()}-${index}-${Math.random()}` }))]);
  }, [game?.recentEvents]);

  useEffect(() => {
    if (game?.status !== 'playing') Swal.close();
  }, [game?.status]);

  useEffect(() => {
    // Multiplayer updates do not include animation events, so synchronize the
    // rendered cards directly whenever the server sends a new game snapshot.
    if (!game || game.recentEvents?.length) return;
    if (game.topCard) setDisplayedTopCard(game.topCard);
    if (game.hand) setDisplayedHand(game.hand);
  }, [game?.gameId, game?.recentEvents?.length, game?.topCard, game?.hand]);

  useEffect(() => {
    const currentEvent = animationQueue[0];
    if (!currentEvent) { setActivePlay(null); return undefined; }
    if (currentEvent.type === 'table') {
      setDisplayedTopCard(currentEvent.card);
      if (currentEvent.hand) setDisplayedHand(currentEvent.hand);
      setAnimationQueue(queue => queue.slice(1));
      return undefined;
    }
    if (currentEvent.type === 'draw') {
      if (game.status !== 'playing') {
        if (currentEvent.hand) setDisplayedHand(currentEvent.hand);
        setActivePlay(null);
        setAnimationQueue(queue => queue.slice(1));
        return undefined;
      }
      setActivePlay(currentEvent);
      const timer = window.setTimeout(() => {
        setDisplayedHand(game.hand || []);
        setAnimationQueue(queue => queue.slice(1));
      }, 800);
      return () => window.clearTimeout(timer);
    }
    if (currentEvent.type === 'alert') {
      setActivePlay(null);
      if (game.status !== 'playing') {
        if (currentEvent.hand) setDisplayedHand(currentEvent.hand);
        setAnimationQueue(queue => queue.slice(1));
        return undefined;
      }
      let cancelled = false;
      Swal.fire({
        toast: true,
        position: 'top',
        icon: 'warning',
        title: currentEvent.message,
        showConfirmButton: false,
        timer: 1400,
        timerProgressBar: true,
        customClass: { popup: 'uno-swal-popup', title: 'uno-swal-title' },
      }).then(() => {
        setDisplayedHand(game.hand || []);
        if (!cancelled) setAnimationQueue(queue => queue.slice(1));
      });
      return () => { cancelled = true; Swal.close(); };
    }
    setDisplayedTopCard(currentEvent.card);
    setActivePlay(currentEvent);
      if (game.status !== 'playing') {
        setDisplayedHand(game.hand || []);
        setActivePlay(null);
      setAnimationQueue(queue => queue.slice(1));
      return undefined;
    }
    let cancelled = false;
    Swal.fire({
      toast: true,
      position: 'top',
      icon: 'warning',
      title: currentEvent.message,
      showConfirmButton: false,
      timer: 1400,
      timerProgressBar: true,
      customClass: { popup: 'uno-swal-popup', title: 'uno-swal-title' },
    }).then(() => {
      setDisplayedHand(game.hand || []);
      if (!cancelled) setAnimationQueue(queue => queue.slice(1));
    });
    return () => { cancelled = true; Swal.close(); };
  }, [animationQueue[0]?.animationId]);

  useEffect(() => {
    if (!game?.gameId || !game.hasDrawnThisTurn || !isYourTurn || busy || activePlay || animationQueue.length) return undefined;
    const timer = window.setTimeout(() => { setWildCard(null); pass(); }, 4000);
    return () => window.clearTimeout(timer);
  }, [game?.gameId, game?.hasDrawnThisTurn, isYourTurn, busy, activePlay, animationQueue.length]);

  if (!game && rejoiningGame) return <main className="landing"><section className="welcome-card"><p className="eyebrow">MULTIPLAYER GAME</p><h1>REJOINING<br /><em>GAME…</em></h1><p className="intro">Restoring your place in the active game.</p></section></main>;

  if (!game) return <main className="landing">
    <div className="landing-decoration decoration-one" /><div className="landing-decoration decoration-two" />
    <header className="brand clickable-brand" role="button" tabIndex="0" onClick={() => window.location.reload()} onKeyDown={e => { if (e.key === 'Enter' || e.key === ' ') window.location.reload(); }}><span className="brand-mark">U</span><span>UNO <b>CIHUY</b></span></header>
    <section className="welcome-card"><p className="eyebrow">Yang HOKI Yang Menang</p><h1>BERANI<br /><em>COBA?</em></h1><p className="intro">Lihat kartu lu, atur strategi. Bantai atau dibantai</p>{terminalMessage && <div className="error-message game-error"><p>{terminalMessage}</p><button type="button" className="subtle-button" onClick={returnToLobby}>Return to lobby</button></div>}
      <form onSubmit={e => { e.preventDefault(); if (mode === 'bot' || (mode === 'friends' && friendsRole)) { if (hasName) createGame(); else setError('Enter your name before starting or hosting a game.'); } }}>
        {!mode && <div className="mode-options"><p className="eyebrow">CHOOSE YOUR GAME</p><button type="button" className="primary-button" onClick={() => setMode('bot')}>Play vs bot <span>→</span></button><button type="button" className="subtle-button" onClick={() => setMode('friends')}>Play with friends</button></div>}
        {lobby && <div className="lobby-panel"><p className="eyebrow">DEMO LOBBY · {lobby.players.length}/{lobby.maxPlayers}</p>{lobby.players.map(player => <p key={player.id}>{player.name}{player.id === lobby.hostPlayerId ? ' · HOST' : ''}</p>)}<small>{lobby.players.length < lobby.minPlayers ? 'Waiting for at least 2 players…' : 'Ready to start'}</small>{lobby.hostPlayerId === playerId && <button type="button" className="primary-button" disabled={busy || lobby.players.length < lobby.minPlayers} onClick={startGame}>Start multiplayer game <span>↗</span></button>}<button type="button" className="subtle-button" disabled={busy} onClick={leaveLobby}>Leave lobby</button></div>}
        {mode === 'friends' && !lobby && !friendsRole && <div className="mode-options"><p className="eyebrow">FRIENDS GAME</p><button type="button" className="primary-button" onClick={() => setFriendsRole('host')}>Host a lobby</button><button type="button" className="subtle-button" onClick={() => setFriendsRole('join')}>Join a lobby</button></div>}
        {mode && !lobby && (mode === 'bot' || friendsRole) && <><label htmlFor="player-name">YOUR NAME</label><input id="player-name" value={name} onChange={e => setName(e.target.value)} placeholder="Enter your name" maxLength="24" /></>}
        {mode && !lobby && (mode === 'bot' || friendsRole) && <>{(mode === 'bot' || friendsRole === 'host') && <><label htmlFor="opponents">OPPONENTS</label><select id="opponents" value={opponents} onChange={e => setOpponents(Number(e.target.value))}><option value="1">1 opponent</option><option value="2">2 opponents</option><option value="3">3 opponents</option></select></>}
        {error && <p className="error-message">{error}</p>}<button className="primary-button" disabled={busy}>{busy ? (mode === 'bot' ? 'Starting game...' : friendsRole === 'host' ? 'Hosting lobby...' : 'Joining lobby...') : (mode === 'bot' ? 'Play vs bot' : friendsRole === 'host' ? 'Host lobby' : 'Join lobby')} <span>↗</span></button><button type="button" className="subtle-button" onClick={() => { setMode(null); setFriendsRole(null); }}>Choose another mode</button></>}
      </form>
    </section>
  </main>;

  const human = game.players.find(p => p.isHuman);
  const humanIndex = game.players.findIndex(player => player.isHuman);
  const opponentsList = humanIndex >= 0
    ? [...game.players.slice(humanIndex + 1), ...game.players.slice(0, humanIndex)]
    : [];
  const canDraw = isYourTurn && !game.canChallengeDrawFour && !matchPaused;
  const directionStep = game.direction === 'Clockwise' ? 1 : -1;
  const currentPlayerIndex = game.players.findIndex(player => player.isCurrentPlayer);
  const nextPlayer = currentPlayerIndex >= 0
    ? game.players[(currentPlayerIndex + directionStep + game.players.length) % game.players.length]
    : null;
  // Seat 0 is always the local player. Other seats are positioned relative to
  // the viewer, so do not use the shared player-list index here.
  const animatedSeat = activePlay && activePlay.playerId === game.humanPlayerId
    ? 0
    : activePlay && opponentsList.findIndex(player => player.id === activePlay.playerId) + 1;
  const inPlayLabel = displayedTopCard?.action
    ? displayedTopCard.action.replace('WildDrawFour', '+4').replace('DrawTwo', '+2')
    : displayedTopCard?.value;
  const inPlayColor = displayedTopCard?.color === 'Wild' ? game.activeColor : displayedTopCard?.color || game.activeColor;
  return <main className="game-shell">
    {connectionStatus !== 'connected' && <div className="error-message game-error">Connection {connectionStatus}. Reconnecting automatically…</div>}
    {matchPaused && <div className="error-message game-error">{game.disconnectedPlayer || 'A player'} disconnected. Waiting for reconnection{game.reconnectSecondsRemaining != null ? ` · ${game.reconnectSecondsRemaining}s remaining` : '…'}</div>}
    <header className="game-header"><a className="brand" href="#" onClick={e => { e.preventDefault(); setGame(null); }}><span className="brand-mark">U</span><span>UNO <b>CIHUY</b></span></a><div className="round-label"><span className="live-dot" /> FRIENDLY GAME <span className="round-divider">/</span> ROUND {String(game.roundNumber).padStart(2, '0')}</div><div className="score-display">SCORE <b>{human.score}</b></div><button className="icon-button" title="Restart game" onClick={restartGame} disabled={busy || Boolean(activePlay) || animationQueue.length > 0}>↻ <span>Restart</span></button></header>
    <section className="table-area">
      <div className="opponent-row">{opponentsList.map((player, index) => <div className={`opponent ${player.isCurrentPlayer ? 'active' : ''}`} key={player.id}>
        <div className={`avatar avatar-${index}`}>{player.name.slice(0, 1).toUpperCase()}</div><div><strong>{player.name}</strong><span>{player.cardCount} {player.cardCount === 1 ? 'card' : 'cards'} · {player.score} pts</span></div>{player.isCurrentPlayer && <span className="turn-badge">THINKING</span>}{player.cardCount === 1 && <button className="catch-button" disabled={busy || Boolean(activePlay) || animationQueue.length > 0 || game.status !== 'playing'} onClick={() => catchUno(player.id)} aria-label={`Catch ${player.name} for not calling UNO`}>CATCH UNO · {player.name}</button>}
      </div>)}</div>
      <span className="next-player">NEXT: <b>{nextPlayer?.name || '—'}</b></span>
      <div className="felt"><div className="table-stitch" /><div className="table-topline"><span>THE TABLE</span><span className="direction">{game.direction === 'Clockwise' ? '↻' : '↺'} &nbsp;{game.direction}</span></div>
        <div className="pile-row"><div className="pile-wrap"><div className="pile-count">{game.drawPileCount}</div><button className="draw-pile" onClick={draw} disabled={!canDraw || busy || Boolean(activePlay) || animationQueue.length > 0} aria-label="Draw a card"><span>U</span><i>UNO</i></button><span className="pile-caption">DRAW PILE</span></div>
          <div className="turn-arrow">→</div><div className="discard-wrap">{displayedTopCard && <CardFace card={displayedTopCard} />}<span className="pile-caption">DISCARD PILE</span></div></div>
        <div className="active-color"><span>IN PLAY</span><i className={`color-dot ${colorsClass[inPlayColor]}`} />{inPlayColor}{inPlayLabel !== undefined && <b> · {inPlayLabel}</b>}</div>
      </div>
    </section>
    <section className="hand-area"><div className="hand-heading"><div><p className="eyebrow">YOUR HAND</p><h2>{human.name}<span>{displayedHand.length} cards</span></h2></div><div className="hand-actions">{game.canChallengeDrawFour && <><button className="subtle-button pass-button" onClick={challengeDrawFour} disabled={busy || Boolean(activePlay) || animationQueue.length > 0}>Challenge +4</button><button className="subtle-button pass-button" onClick={draw} disabled={busy || Boolean(activePlay) || animationQueue.length > 0}>Draw 4</button></>}{isYourTurn && game.hasDrawnThisTurn && <button className="subtle-button pass-button" onClick={pass} disabled={busy || Boolean(activePlay) || animationQueue.length > 0}>Pass</button>}<button className="subtle-button uno-button" onClick={uno} disabled={!isYourTurn || busy || Boolean(activePlay) || animationQueue.length > 0 || displayedHand.length !== 2}>✳ &nbsp;Call UNO</button></div></div>
      <div className="hand-cards">{displayedHand.map(card => <CardFace key={card.id} card={card} disabled={!isYourTurn || busy || Boolean(activePlay) || animationQueue.length > 0} onClick={() => card.color === 'Wild' ? setWildCard(card) : play(card)} />)}</div>
      {error && <p className="error-message game-error">{error}</p>}
    </section>
    {activePlay?.type === 'draw' && <div className={`draw-flight from-seat-${animatedSeat}`} key={activePlay.animationId} aria-hidden="true"><span>{activePlay.playerName}</span><div className="draw-pile draw-pile-animation"><span>U</span><i>UNO</i></div></div>}
    {activePlay?.type === 'play' && <div className={`play-flight from-seat-${animatedSeat}`} key={activePlay.animationId} aria-hidden="true"><span>{activePlay.playerName}</span><CardFace card={activePlay.card} disabled /></div>}
    {wildCard && <div className="modal-backdrop" role="presentation" onClick={() => setWildCard(null)}><section className="color-modal" role="dialog" aria-modal="true" aria-labelledby="color-title" onClick={e => e.stopPropagation()}><button className="close-modal" onClick={() => setWildCard(null)}>×</button><p className="eyebrow">A WILD MOVE</p><h2 id="color-title">Choose a color</h2><p>Set the color that continues the round.</p><div className="color-options">{colors.map(color => <button key={color} className={`color-choice ${colorsClass[color]}`} onClick={() => { const selected = wildCard; setWildCard(null); play(selected, color); }}><span />{color}</button>)}</div></section></div>}
    {game.requiresOpeningColorChoice && <div className="modal-backdrop"><section className="color-modal" role="dialog" aria-modal="true" aria-labelledby="opening-color-title"><p className="eyebrow">OPENING WILD</p><h2 id="opening-color-title">Choose a color</h2><p>Choose the color before making the first play.</p><div className="color-options">{colors.map(color => <button key={color} className={`color-choice ${colorsClass[color]}`} onClick={() => chooseOpeningColor(color)} disabled={busy}><span />{color}</button>)}</div></section></div>}
    {(game.status === 'round-complete' || game.status === 'completed') && <div className="modal-backdrop"><section className="color-modal result-modal"><div className="result-star">✳</div><p className="eyebrow">{game.status === 'completed' ? 'GAME COMPLETE' : 'ROUND COMPLETE'}</p><h2>{game.status === 'completed' ? `${game.winner || 'A player'} wins the game!` : `${game.winner} wins the round!`}</h2><div className="score-summary">{game.players.map(player => <div key={player.id}><span>{player.name}</span><b>{player.score} pts</b></div>)}</div>{game.status === 'round-complete' && (lobby?.hostPlayerId === playerId ? <button className="primary-button" onClick={() => restart(false)}>Next round <span>↻</span></button> : <p>Waiting for the host to start the next round…</p>)}{game.status === 'completed' && <button className="primary-button" onClick={() => restart(true)}>Play again <span>↻</span></button>}</section></div>}
  </main>;
}

export default App;
