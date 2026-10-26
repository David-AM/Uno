const colorsClass = { Red: 'red', Blue: 'blue', Green: 'green', Yellow: 'yellow', Wild: 'wild' };

export default function CardFace({ card, small = false, onClick, disabled }) {
  const label = card.action ? card.action.replace('WildDrawFour', '+4').replace('DrawTwo', '+2') : card.value;
  const symbolAction = card.action === 'Skip' || card.action === 'Reverse';

  return (
    <button
      type="button"
      disabled={disabled}
      onClick={onClick}
      className={`playing-card ${colorsClass[card.color]} ${symbolAction ? 'symbol-action' : ''} ${small ? 'small' : ''} ${disabled ? 'disabled' : ''}`}
      aria-label={`${card.color} ${card.action || card.value}`}
    >
      <span className="card-corner">{label}</span>
      <span className="card-center">{label}</span>
      <span className="card-corner bottom">{label}</span>
    </button>
  );
}
