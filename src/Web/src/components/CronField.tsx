import { useMemo } from 'react';
import { describeCron, validateCron } from '../lib/cron';

const PRESETS: Array<{ label: string; pattern: string }> = [
  { label: 'Per 30 min', pattern: '0 */30 * * * *' },
  { label: 'Per hour', pattern: '0 0 * * * *' },
  { label: 'Per 3 hours', pattern: '0 0 */3 * * *' },
  { label: 'Per day 02:00', pattern: '0 0 2 * * *' },
  { label: 'Per week 02:00', pattern: '0 0 2 * * MON' },
];

interface CronFieldProps {
  value: string;
  onChange: (value: string) => void;
}

export default function CronField({ value, onChange }: CronFieldProps) {
  const error = useMemo(() => validateCron(value), [value]);
  const description = error === null ? describeCron(value) : null;

  return (
    <div className="cron">
      <div className="cron-presets">
        {PRESETS.map((preset) => (
          <button
            key={preset.pattern}
            type="button"
            className={`btn chip${value.trim().toUpperCase() === preset.pattern.toUpperCase() ? ' active' : ''}`}
            onClick={() => onChange(preset.pattern)}
          >
            {preset.label}
          </button>
        ))}
      </div>
      <input
        required
        spellCheck={false}
        value={value}
        onChange={(e) => onChange(e.target.value)}
        placeholder="0 0 2 * * *"
        className={error ? 'invalid' : undefined}
      />
      {error ? (
        <p role="alert" className="cron-error">{error}</p>
      ) : (
        <p className="cron-desc">{description ?? 'Runs on a custom schedule (Philippine time)'}</p>
      )}
      <small>
        Six fields: <code>seconds minutes hours day-of-month month day-of-week</code>.
        Run times are in Philippine time.
      </small>
    </div>
  );
}
