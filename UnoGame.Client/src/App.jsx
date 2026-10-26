import { useState } from 'react';
import 'sweetalert2/dist/sweetalert2.min.css';
import Lobby from './components/Lobby.jsx';
import Table from './components/Table.jsx';
import Hand from './components/Hand.jsx';
import WildColorModal from './components/WildColorModal.jsx';
import ResultScreen from './components/ResultScreen.jsx';
import { useGameConnection } from './hooks/useGameConnection.js';
import { useAnimationQueue } from './hooks/useAnimationQueue.js';
import { useGameActions } from './hooks/useGameActions.js';

function App() {
  const [error, setError] = useState('');
  const {
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
  } = useGameConnection({ setError });

  const { animationQueue, activePlay, displayedTopCard, displayedHand } = useAnimationQueue(game);

  const {
    busy,
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
  } = useGameActions({
    game,
    playerId,
    connection,
    setGame,
    activePlay,
    animationQueue,
    blocked: Boolean(activePlay) || animationQueue.length > 0 || Boolean(game?.isPaused),
    error,
    setError,
  });

  const hasName = name.trim().length > 0;

  if (!game && rejoiningGame) {
    return (
      <main className="landing">
        <section className="welcome-card">
          <p className="eyebrow">MULTIPLAYER GAME</p>
          <h1>REJOINING<br /><em>GAME…</em></h1>
          <p className="intro">Restoring your place in the active game.</p>
        </section>
      </main>
    );
  }

  if (!game) {
    return (
      <Lobby
        terminalMessage={terminalMessage}
        lobby={lobby}
        playerId={playerId}
        friendsRole={friendsRole}
        name={name}
        setName={setName}
        opponents={opponents}
        setOpponents={setOpponents}
        hasName={hasName}
        busy={busy}
        error={error}
        hostOrJoin={hostOrJoin}
        leaveLobby={leaveLobby}
        returnToLobby={returnToLobby}
        startGame={startGame}
        setFriendsRole={setFriendsRole}
        setError={setError}
      />
    );
  }

  const eligiblePlayers = game.players.filter(player => !player.isForfeited);
  const human = eligiblePlayers.find(p => p.isHuman) || game.players.find(p => p.isHuman);
  const humanIndex = eligiblePlayers.findIndex(player => player.isHuman);
  const opponentsList = humanIndex >= 0
    ? [...eligiblePlayers.slice(humanIndex + 1), ...eligiblePlayers.slice(0, humanIndex)]
    : [];
  const directionStep = game.direction === 'Clockwise' ? 1 : -1;
  const currentPlayerIndex = eligiblePlayers.findIndex(player => player.isCurrentPlayer);
  const nextPlayer = currentPlayerIndex >= 0 && eligiblePlayers.length > 1
    ? eligiblePlayers[(currentPlayerIndex + directionStep + eligiblePlayers.length) % eligiblePlayers.length]
    : null;

  return (
    <main className="game-shell">
      <Table
        game={game}
        connectionStatus={connectionStatus}
        matchPaused={matchPaused}
        human={human}
        eligiblePlayers={eligiblePlayers}
        nextPlayer={nextPlayer}
        activePlay={activePlay}
        busy={busy}
        animationQueue={animationQueue}
        displayedTopCard={displayedTopCard}
        draw={draw}
        catchUno={catchUno}
        pass={pass}
        challengeDrawFour={challengeDrawFour}
        setWildCard={setWildCard}
        play={play}
        chooseOpeningColor={chooseOpeningColor}
        wildCard={wildCard}
        playerId={playerId}
        lobby={lobby}
        returnToLobby={returnToLobby}
        restart={restart}
        restartGame={restartGame}
        setGame={setGame}
      />
      <Hand
        game={game}
        human={human}
        displayedHand={displayedHand}
        busy={busy}
        error={error}
        activePlay={activePlay}
        animationQueue={animationQueue}
        isYourTurn={isYourTurn}
        draw={draw}
        challengeDrawFour={challengeDrawFour}
        pass={pass}
        sayUno={sayUno}
        setWildCard={setWildCard}
        play={play}
      />
      <WildColorModal
        wildCard={wildCard}
        setWildCard={setWildCard}
        play={play}
        chooseOpeningColor={chooseOpeningColor}
        game={game}
        busy={busy}
      />
      <ResultScreen
        game={game}
        lobby={lobby}
        playerId={playerId}
        restart={restart}
        returnToLobby={returnToLobby}
      />
    </main>
  );
}

export default App;
