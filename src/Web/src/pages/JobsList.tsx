import { useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { useAuth } from '../main';
import { apiFetch } from '../oidc';
import { type JobDto, type RunResult } from '../types';

const fmt = (iso?: string | null) => (iso ? new Date(iso).toLocaleString() : '—');

export default function JobsList() {
  const { user } = useAuth();
  const [jobs, setJobs] = useState<JobDto[]>([]);
  const [error, setError] = useState('');
  const [flash, setFlash] = useState('');

  const load = async () => {
    if (!user) return;
    const resp = await apiFetch('/api/jobs', user);
    if (resp.ok) setJobs(await resp.json());
    else setError(`Failed to load jobs (${resp.status})`);
  };

  useEffect(() => { load(); }, [user]);

  const runNow = async (job: JobDto) => {
    setFlash('Running…');
    const resp = await apiFetch(`/api/jobs/${job.id}/run`, user, { method: 'POST' });
    const result: RunResult = await resp.json();
    setFlash(result.success
      ? `OK (${result.attempts} attempt(s), status ${result.http_status})`
      : `Failed after ${result.attempts} attempt(s): ${result.error ?? 'unknown'}`);
    await load();
  };

  const toggle = async (job: JobDto) => {
    await apiFetch(`/api/jobs/${job.id}`, user, { method: 'PUT', body: JSON.stringify({ ...job, enabled: !job.enabled }) });
    await load();
  };

  const remove = async (job: JobDto) => {
    if (!confirm(`Delete job "${job.name}"?`)) return;
    await apiFetch(`/api/jobs/${job.id}`, user, { method: 'DELETE' });
    await load();
  };

  if (!user) return <p>Please log in to manage jobs.</p>;

  return (
    <section>
      <div className="toolbar">
        <h2>Jobs</h2>
        <Link className="btn primary" to="/jobs/new">+ New job</Link>
      </div>
      {error && <p className="error">{error}</p>}
      {flash && <p className="flash">{flash}</p>}
      <table>
        <thead>
          <tr>
            <th>Name</th>
            <th>Schedule</th>
            <th>Timezone</th>
            <th>Next run</th>
            <th>Last run</th>
            <th>Auth</th>
            <th>Enabled</th>
            <th></th>
          </tr>
        </thead>
        <tbody>
          {jobs.map((job) => (
            <tr key={job.id}>
              <td><Link to={`/jobs/${job.id}`}>{job.name}</Link></td>
              <td><code>{job.cronExpression}</code></td>
              <td>{job.timezone}</td>
              <td>{fmt(job.nextRunUtc)}</td>
              <td>
                {job.lastRun ? (
                  <>
                    <span className={`status ${job.lastRun.status.toLowerCase()}`}>{job.lastRun.status}</span>{' '}
                    <span className="muted">{fmt(job.lastRun.scheduledForUtc)}</span>
                  </>
                ) : '—'}
              </td>
              <td>{job.authMode === 'arauth' ? 'Atlas Rig' : 'Anon'}</td>
              <td>
                <button className="btn" onClick={() => toggle(job)}>{job.enabled ? 'Disable' : 'Enable'}</button>
              </td>
              <td className="actions">
                <button className="btn" onClick={() => runNow(job)}>Run now</button>
                <Link className="btn" to={`/jobs/${job.id}/history`}>History</Link>
                <button className="btn danger" onClick={() => remove(job)}>Delete</button>
              </td>
            </tr>
          ))}
        </tbody>
      </table>
      {jobs.length === 0 && <p className="muted">No jobs yet — create one.</p>}
    </section>
  );
}
