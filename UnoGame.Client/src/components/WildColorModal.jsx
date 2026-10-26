const colors = ['Red', 'Blue', 'Green', 'Yellow'];
const colorsClass = { Red: 'red', Blue: 'blue', Green: 'green', Yellow: 'yellow', Wild: 'wild' };

export default function WildColorModal({ wildCard, setWildCard, play, chooseOpeningColor, game, busy }) {
  if (wildCard) {
    return (
      <div className="modal-backdrop" role="presentation" onClick={() => setWildCard(null)}>
        <section className="color-modal" role="dialog" aria-modal="true" aria-labelledby="color-title" onClick={e => e.stopPropagation()}>
          <button className="close-modal" onClick={() => setWildCard(null)}>×</button>
          <p className="eyebrow">A WILD MOVE</p>
          <h2 id="color-title">Choose a color</h2>
          <p>Set the color that continues the round.</p>
          <div className="color-options">{colors.map(color => <button key={color} className={`color-choice ${colorsClass[color]}`} onClick={() => { const selected = wildCard; setWildCard(null); play(selected, color); }}><span />{color}</button>)}</div>
        </section>
      </div>
    );
  }

  if (game.requiresOpeningColorChoice) {
    return (
      <div className="modal-backdrop">
        <section className="color-modal" role="dialog" aria-modal="true" aria-labelledby="opening-color-title">
          <p className="eyebrow">OPENING WILD</p>
          <h2 id="opening-color-title">Choose a color</h2>
          <p>Choose the color before making the first play.</p>
          <div className="color-options">{colors.map(color => <button key={color} className={`color-choice ${colorsClass[color]}`} onClick={() => chooseOpeningColor(color)} disabled={busy}><span />{color}</button>)}</div>
        </section>
      </div>
    );
  }

  return null;
}
