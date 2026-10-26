import CardFace from './CardFace.jsx';

const colors = ['Red', 'Blue', 'Green', 'Yellow'];
const colorsClass = { Red: 'red', Blue: 'blue', Green: 'green', Yellow: 'yellow', Wild: 'wild' };

export default function Lobby({
  terminalMessage,
  lobby,
  playerId,
  friendsRole,
  name,
  setName,
  opponents,
  setOpponents,
  hasName,
  busy,
  error,
  hostOrJoin,
  leaveLobby,
  returnToLobby,
  startGame,
  setFriendsRole,
  setError,
}) {
  return (
    <main className="landing">
      <div className="landing-decoration decoration-one" />
      <div className="landing-decoration decoration-two" />
      <header className="brand clickable-brand" role="button" tabIndex="0" onClick={() => window.location.reload()} onKeyDown={e => { if (e.key === 'Enter' || e.key === ' ') window.location.reload(); }}>
        <span className="brand-mark">U</span>
        <span>UNO <b>CIHUY</b></span>
      </header>
      <section className="welcome-card">
        <p className="eyebrow">Yang HOKI Yang Menang</p>
        <h1>HAVE<br /><em>FUN!!</em></h1>
        <p className="intro">Lihat kartu lu, atur strategi. jadi pemenang boiii</p>
        {terminalMessage && <div className="error-message game-error"><p>{terminalMessage}</p><button type="button" className="subtle-button" onClick={returnToLobby}>Return to lobby</button></div>}
        <form onSubmit={e => { e.preventDefault(); if (friendsRole) { if (hasName) hostOrJoin(); else setError('Enter your name before hosting or joining a game.'); } }}>
          {!friendsRole && !lobby && <div className="mode-options"><p className="eyebrow">MULTIPLAYER GAME</p><button type="button" className="primary-button" onClick={() => setFriendsRole('host')}>Host a lobby <span>→</span></button><button type="button" className="subtle-button" onClick={() => setFriendsRole('join')}>Join a lobby</button></div>}
          {lobby && <div className="lobby-panel"><p className="eyebrow">DEMO LOBBY · {lobby.players.length}/{lobby.maxPlayers}</p>{lobby.players.map(player => <p key={player.id}>{player.name}{player.id === lobby.hostPlayerId ? ' · HOST' : ''}</p>)}<small>{lobby.players.length < lobby.minPlayers ? 'Waiting for at least 2 players…' : 'Ready to start'}</small>{lobby.hostPlayerId === playerId && <button type="button" className="primary-button" disabled={busy || lobby.players.length < lobby.minPlayers} onClick={startGame}>Start multiplayer game <span>↗</span></button>}<button type="button" className="subtle-button" disabled={busy} onClick={leaveLobby}>Leave lobby</button></div>}
          {!lobby && friendsRole && <><label htmlFor="player-name">YOUR NAME</label><input id="player-name" value={name} onChange={e => setName(e.target.value)} placeholder="Enter your name" maxLength="24" /></>}
          {!lobby && friendsRole && <>{friendsRole === 'host' && <><label htmlFor="opponents">OPPONENTS</label><select id="opponents" value={opponents} onChange={e => setOpponents(Number(e.target.value))}><option value="1">1 opponent</option><option value="2">2 opponents</option><option value="3">3 opponents</option></select></>}
          {error && <p className="error-message">{error}</p>}<button className="primary-button" disabled={busy}>{busy ? friendsRole === 'host' ? 'Hosting lobby...' : 'Joining lobby...' : friendsRole === 'host' ? 'Host lobby' : 'Join lobby'} <span>↗</span></button><button type="button" className="subtle-button" onClick={() => setFriendsRole(null)}>Choose another option</button></>}
        </form>
      </section>
    </main>
  );
}
