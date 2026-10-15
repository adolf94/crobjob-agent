const DOW_NAMES = ['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat', 'Sun'];
const MONTH_NAMES = ['Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', 'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'];

const FIELD_BOUNDS: Array<[number, number]> = [
  [0, 59],
  [0, 59],
  [0, 23],
  [1, 31],
  [1, 12],
  [0, 7],
];
const FIELD_NAMES = ['seconds', 'minutes', 'hours', 'day-of-month', 'month', 'day-of-week'];
const FIELD_RE = /^[0-9A-Za-z*/,-]+$/;
const ATOM_RE = /^\d{1,2}$|^[A-Za-z]{3}$/;

const pad = (n: number) => String(n).padStart(2, '0');

export function validateCron(expression: string): string | null {
  const trimmed = expression.trim();
  if (!trimmed) return 'Cron expression is required.';
  const parts = trimmed.split(/\s+/);
  if (parts.length !== 6)
    return 'Needs exactly 6 fields: seconds minutes hours day-of-month month day-of-week.';

  for (let i = 0; i < 6; i++) {
    const field = parts[i];
    const name = FIELD_NAMES[i];
    if (!FIELD_RE.test(field)) return `${name}: invalid characters.`;
    for (const atom of field.split(',')) {
      const sections = atom.split('/');
      if (sections.length > 2) return `${name}: too many "/" in "${atom}".`;
      const base = sections[0];
      const step = sections[1];
      if (step !== undefined && step !== '') {
        if (!/^\d+$/.test(step) || Number(step) < 1) return `${name}: invalid step "${step}".`;
      }
      if (base === '') return `${name}: empty value.`;
      if (base === '*') continue;
      const ends = base.split('-');
      if (ends.length > 2 || !ends.every((v) => ATOM_RE.test(v)))
        return `${name}: invalid value "${base}".`;
      if (ends.every((v) => /^\d{1,2}$/.test(v))) {
        const [lo, hi] = FIELD_BOUNDS[i];
        const values = ends.map(Number);
        if (values.some((n) => n < lo || n > hi))
          return `${name}: ${base} is outside ${lo}-${hi}.`;
        if (values.length === 2 && values[0] > values[1])
          return `${name}: range "${base}" starts above its end.`;
      }
    }
  }
  return null;
}

const stepOf = (token: string): number | null => {
  const m = /^(?:\*|0)\/(\d+)$/.exec(token);
  return m ? Number(m[1]) : null;
};

const listOf = (token: string, lo: number, hi: number): number[] | null => {
  if (!/^\d+(,\d+)*$/.test(token)) return null;
  const values = token.split(',').map(Number);
  return values.every((n) => n >= lo && n <= hi) ? values : null;
};

const dowLabel = (token: string): string | null => {
  if (/^\d$/.test(token)) {
    const n = Number(token);
    return n > 7 ? null : n === 7 ? 'Sun' : DOW_NAMES[n];
  }
  const idx = DOW_NAMES.findIndex((name) => name.toUpperCase() === token.toUpperCase());
  return idx >= 0 && idx <= 6 ? DOW_NAMES[idx] : null;
};

const monthLabel = (token: string): string | null => {
  if (/^\d{1,2}$/.test(token)) {
    const n = Number(token);
    return n >= 1 && n <= 12 ? MONTH_NAMES[n - 1] : null;
  }
  const idx = MONTH_NAMES.findIndex((name) => name.toUpperCase() === token.toUpperCase());
  return idx >= 0 ? MONTH_NAMES[idx] : null;
};

const listLabels = (part: string, toLabel: (token: string) => string | null): string | null => {
  if (part === '*') return null;
  const items: string[] = [];
  for (const token of part.split(',')) {
    const ends = token.replace(/\/\d+$/, '').split('-');
    if (ends.length > 2) return null;
    const labels = ends.map(toLabel);
    if (labels.length === 1) {
      if (!labels[0]) return null;
      items.push(labels[0]);
    } else {
      if (!labels[0] || !labels[1]) return null;
      items.push(`${labels[0]}-${labels[1]}`);
    }
  }
  return items.join(', ');
};

export function describeCron(expression: string): string | null {
  let parts = expression.trim().split(/\s+/);
  if (parts.length === 5) parts = ['0', ...parts];
  if (parts.length !== 6) return null;
  const [sec, min, hour, dom, month, dow] = parts;

  const dowLabels = listLabels(dow, dowLabel);
  const domNums = listOf(dom, 1, 31);
  const monthLabels = listLabels(month, monthLabel);

  const secStep = stepOf(sec);
  const minStep = stepOf(min);
  const hourStep = stepOf(hour);
  const secNums = listOf(sec, 0, 59);
  const minNums = listOf(min, 0, 59);
  const hourNums = listOf(hour, 0, 23);

  let frequency: string | null = null;
  if (sec === '0') {
    if (min === '*' && hour === '*') frequency = 'every minute';
    else if (hour === '*' && minStep !== null) frequency = `every ${minStep} minutes`;
    else if (min === '0' && hour === '*') frequency = 'every hour';
    else if (hourStep !== null) {
      const minuteText =
        min === '0' ? 'on the hour' : minNums === null ? null : `at minute ${minNums.join(', ')}`;
      frequency = minuteText === null ? null : `every ${hourStep} hours ${minuteText}`;
    }
  }

  if (frequency === null && min === '*' && hour === '*') {
    if (sec === '*') frequency = 'every second';
    else if (secStep !== null) frequency = `every ${secStep} seconds`;
    else if (secNums !== null) frequency = `seconds ${secNums.join(', ')} of every minute`;
  }

  if (
    frequency === null &&
    secNums !== null &&
    minNums !== null &&
    hourNums !== null &&
    secNums.length === 1 &&
    minNums.length === 1 &&
    hourNums.length === 1
  ) {
    frequency = `at ${pad(hourNums[0])}:${pad(minNums[0])}${secNums[0] ? `:${pad(secNums[0])}` : ''}`;
  }

  if (frequency === null) return null;

  let when = '';
  if (dom !== '*') {
    if (domNums === null || dow !== '*') return null;
    when = ` on day ${domNums.join(', ')}`;
  } else if (dow !== '*') {
    if (dowLabels === null) return null;
    when = ` on ${dowLabels}`;
  }
  if (month !== '*') {
    if (monthLabels === null) return null;
    when += ` in ${monthLabels.toLowerCase()}`;
  }
  return `Runs ${frequency}${when}`;
}
