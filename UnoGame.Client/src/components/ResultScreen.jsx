export default function ResultScreen({ game, lobby, playerId, restart, returnToLobby }) {
  if (game.status !== 'round-complete' && game.status !== 'completed') return null;

  return (
    <div className="modal-backdrop">
      <section className="color-modal result-modal">
        <div className="result-star">✳</div>
        <p className="eyebrow">{game.status === 'completed' ? 'GAME COMPLETE' : 'ROUND COMPLETE'}</p>
        <h2>{game.status === 'completed' ? `${game.winner || 'A player'} wins the game!` : `${game.winner} wins the round!`}</h2>
        <div className="score-summary">{game.players.map(player => <div key={player.id}><span>{player.name}</span><b>{player.score} pts</b></div>)}</div>
        {game.status === 'round-complete' && (lobby?.hostPlayerId === playerId ? <button className="primary-button" onClick={() => restart(false)}>Next round <span>↻</span></button> : <p>Waiting for the host to start the next round…</p>)}
        {game.status === 'completed' && <button className="primary-button" onClick={returnToLobby}>Return to lobby <span>↩</span></button>}
      </section>
    </div>
  );
}
