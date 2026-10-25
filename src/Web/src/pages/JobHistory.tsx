import { useEffect, useState } from 'react';
import { Link, useParams } from 'react-router-dom';
import { useAuth } from '../main';
import { api } from '../lib/api';
import { type RunRecord } from '../types';

const fmt = (iso?: string | null) => (iso ? new Date(iso).toLocaleString() : '—');

export default function JobHistory() {
  const { user } = useAuth();
  const { id } = useParams();
  const [runs, setRuns] = useState<RunRecord[]>([]);

  useEffect(() => {
    if (!user || !id) return;
    api.get<RunRecord[]>(`/jobs/${id}/runs?limit=100`)
      .then(({ data }) => setRuns(data))
      .catch(() => { /* history stays empty on failure */ });
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
