import { useEffect, useState } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import CronField from '../components/CronField';
import { validateCron } from '../lib/cron';
import { useAuth } from '../main';
import { apiFetch } from '../oidc';
import { type JobDto, type JobInput } from '../types';

const JOB_TIMEZONE = 'Asia/Manila';
export const CATCHUP_NEVER = 0;
export const CATCHUP_RUN_ONCE = -1;

export default function JobEdit() {
  const { user } = useAuth();
  const { id } = useParams();
  const nav = useNavigate();
  const isEdit = !!id;

  const [form, setForm] = useState<JobInput>({
    name: '',
    targetUrl: '',
    cronExpression: '0 0 2 * * *',
    timezone: JOB_TIMEZONE,
    enabled: true,
    catchupDays: CATCHUP_RUN_ONCE,
    maxRetries: 3,
    appendDateParam: false,
    authMode: 'none',
    authScope: '',
  });
  const [errors, setErrors] = useState<string[]>([]);
  const [saving, setSaving] = useState(false);

  useEffect(() => {
    if (!isEdit || !user) return;
    apiFetch(`/api/jobs/${id}`, user).then(async (r) => {
      if (!r.ok) return;
      const job: JobDto = await r.json();
      setForm({
        name: job.name,
        targetUrl: job.targetUrl,
        cronExpression: job.cronExpression,
        timezone: JOB_TIMEZONE,
        enabled: job.enabled,
        catchupDays: job.catchupDays,
        maxRetries: job.maxRetries,
        appendDateParam: job.appendDateParam,
        authMode: job.authMode,
        authScope: job.authScope ?? '',
      });
    });
  }, [id, user, isEdit]);

  const set = (field: keyof JobInput, value: string | number | boolean) =>
    setForm((f) => ({ ...f, [field]: value }));

  const save = async () => {
    if (!user) return;
    const cronError = validateCron(form.cronExpression);
    if (cronError) {
      setErrors([`Cron: ${cronError}`]);
      return;
    }
    setSaving(true);
    setErrors([]);
    const resp = await apiFetch(isEdit ? `/api/jobs/${id}` : '/api/jobs', user, {
      method: isEdit ? 'PUT' : 'POST',
      body: JSON.stringify(form),
    });
    setSaving(false);
    if (resp.ok) { nav('/'); return; }
    const problem = await resp.json().catch(() => null);
    if (problem?.errors) {
      setErrors(Object.values(problem.errors as Record<string, string[]>).flat());
    } else {
      setErrors([`Save failed (${resp.status})`]);
    }
  };

  if (!user) return <p>Please log in.</p>;

  return (
    <section>
      <h2>{isEdit ? `Edit job #${id}` : 'New job'}</h2>
      {errors.length > 0 && (
        <ul className="error">{errors.map((e, i) => <li key={i}>{e}</li>)}</ul>
      )}
      <form onSubmit={(e) => { e.preventDefault(); save(); }}>
        <label>Name
          <input required value={form.name} onChange={(e) => set('name', e.target.value)} placeholder="interest-accrual" />
        </label>
        <label>Target URL
          <input required value={form.targetUrl} onChange={(e) => set('targetUrl', e.target.value)}
            placeholder="https://app.example.com/api/job?code=<KEY>" />
          <small>POST target. Anonymous endpoints carry their key (e.g. ?code=); Atlas Rig jobs authenticate with the app's own client credentials.</small>
        </label>
        <label>Cron expression (times are Philippine time)
          <CronField value={form.cronExpression} onChange={(v) => set('cronExpression', v)} />
        </label>
        <label>Timezone
          <input readOnly value="Asia/Manila (UTC+08:00, Philippine time)" />
          <small>Fixed system-wide to Philippine time.</small>
        </label>
        <div className="catchup">
          <span className="field-title">Catch-up policy (what happens if a scheduled run was missed)</span>
          <label className="check">
            <input type="radio" name="catchup" checked={form.catchupDays === CATCHUP_NEVER}
              onChange={() => set('catchupDays', CATCHUP_NEVER)} />
            Do not catch up — skip missed runs
          </label>
          <label className="check">
            <input type="radio" name="catchup" checked={form.catchupDays === CATCHUP_RUN_ONCE}
              onChange={() => set('catchupDays', CATCHUP_RUN_ONCE)} />
            Run once — dispatch a single catch-up run
          </label>
          {form.catchupDays > 0 && (
            <label className="check">
              <input type="radio" name="catchup" checked readOnly />
              Legacy back-fill window (existing job):
              <input type="number" min={1} max={30} value={form.catchupDays}
                onChange={(e) => set('catchupDays', +e.target.value)} style={{ maxWidth: '5rem' }} />
            </label>
          )}
        </div>
        <div className="row">
          <label>Max retries
            <input type="number" min={0} max={20} value={form.maxRetries} onChange={(e) => set('maxRetries', +e.target.value)} />
          </label>
        </div>
        <label className="check">
          <input type="checkbox" checked={form.appendDateParam} onChange={(e) => set('appendDateParam', e.target.checked)} />
          Append <code>&date=YYYY-MM-DD</code> (local date) to each dispatch
        </label>
        <label className="check">
          <input type="checkbox" checked={form.enabled} onChange={(e) => set('enabled', e.target.checked)} />
          Enabled
        </label>
        <fieldset>
          <legend>Target authentication</legend>
          <label className="check">
            <input type="radio" name="am" checked={form.authMode === 'none'} onChange={() => set('authMode', 'none')} />
            Anonymous (function key in URL)
          </label>
          <div className="row">
            <label className="check">
              <input type="radio" name="am" checked={form.authMode === 'arauth'} onChange={() => set('authMode', 'arauth')} />
              Atlas Rig (client credentials)
            </label>
          </div>
          {form.authMode === 'arauth' && (
            <label>Scope
              <input value={form.authScope ?? ''} onChange={(e) => set('authScope', e.target.value)} placeholder="api://target-client/run:job" />
              <small>The app's own Atlas Rig client credentials are configured server-side.</small>
            </label>
          )}
        </fieldset>
        <div className="row">
          <button className="btn primary" type="submit" disabled={saving}>{saving ? 'Saving…' : 'Save'}</button>
          <button className="btn" type="button" onClick={() => nav('/')}>Cancel</button>
        </div>
      </form>
    </section>
  );
}
