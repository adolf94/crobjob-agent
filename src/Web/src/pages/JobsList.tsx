import { useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { useAuth } from '../main';
import { api, errStatus } from '../lib/api';
import { type JobDto, type RunResult } from '../types';

const fmt = (iso?: string | null) => (iso ? new Date(iso).toLocaleString() : '—');

export default function JobsList() {
  const { user } = useAuth();
  const [jobs, setJobs] = useState<JobDto[]>([]);
  const [error, setError] = useState('');
  const [flash, setFlash] = useState('');

  const load = async () => {
    if (!user) return;
    try {
      const { data } = await api.get<JobDto[]>('/jobs');
      setJobs(data);
    } catch (err) {
      setError(`Failed to load jobs (${errStatus(err)})`);
    }
  };

  useEffect(() => { load(); }, [user]);

  const runNow = async (job: JobDto) => {
    setFlash('Running…');
    try {
      const { data: result } = await api.post<RunResult>(`/jobs/${job.id}/run`);
      setFlash(result.success
        ? `OK (${result.attempts} attempt(s), status ${result.http_status})`
        : `Failed after ${result.attempts} attempt(s): ${result.error ?? 'unknown'}`);
    } catch (err) {
      setFlash(`Run failed (${errStatus(err)})`);
    }
    await load();
  };

  const toggle = async (job: JobDto) => {
    try {
      await api.put(`/jobs/${job.id}`, { ...job, enabled: !job.enabled });
    } catch { /* banner comes from the next load */ }
    await load();
  };

  const remove = async (job: JobDto) => {
    if (!confirm(`Delete job "${job.name}"?`)) return;
    try {
      await api.delete(`/jobs/${job.id}`);
    } catch { /* banner comes from the next load */ }
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
