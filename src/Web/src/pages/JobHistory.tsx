import { useEffect, useState } from 'react';
import { Link, useParams } from 'react-router-dom';
import { useAuth } from '../main';
import { apiFetch } from '../oidc';
import { type RunRecord } from '../types';

const fmt = (iso?: string | null) => (iso ? new Date(iso).toLocaleString() : '—');

export default function JobHistory() {
  const { user } = useAuth();
  const { id } = useParams();
  const [runs, setRuns] = useState<RunRecord[]>([]);

  useEffect(() => {
    if (!user || !id) return;
    apiFetch(`/api/jobs/${id}/runs?limit=100`, user).then(async (r) => {
      if (r.ok) setRuns(await r.json());
    });
  }, [id, user]);

  if (!user) return <p>Please log in.</p>;

  return (
    <section>
      <div className="toolbar">
        <h2>Run history — job #{id}</h2>
        <Link className="btn" to="/">Back</Link>
      </div>
      <table>
        <thead>
          <tr>
            <th>Scheduled for</th>
            <th>Attempt</th>
            <th>Status</th>
            <th>HTTP</th>
            <th>Started</th>
            <th>Finished</th>
            <th>Error</th>
          </tr>
        </thead>
        <tbody>
          {runs.map((r) => (
            <tr key={r.id}>
              <td>{fmt(r.scheduledForUtc)}</td>
              <td>{r.attempt}</td>
              <td><span className={`status ${r.status.toLowerCase()}`}>{r.status}</span></td>
              <td>{r.httpStatus ?? '—'}</td>
              <td>{fmt(r.startedAtUtc)}</td>
              <td>{fmt(r.finishedAtUtc)}</td>
              <td className="muted">{r.error ?? ''}</td>
            </tr>
          ))}
        </tbody>
      </table>
      {runs.length === 0 && <p className="muted">No runs recorded yet.</p>}
    </section>
  );
}
