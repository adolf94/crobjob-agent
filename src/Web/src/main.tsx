import React, { useEffect, useState } from 'react';
import ReactDOM from 'react-dom/client';
import { BrowserRouter, Routes, Route, Link, useNavigate } from 'react-router-dom';
import { getUser, login, logout, completeLogin, BASE_URL, type User } from './oidc';
import JobsList from './pages/JobsList';
import JobEdit from './pages/JobEdit';
import JobHistory from './pages/JobHistory';
import './index.css';

// React-router basename derived from the app root (APP_ROOT in CI/Docker).
const basename = BASE_URL || '/';

type AuthState = { user: User | null; loading: boolean; setUser: (user: User | null) => void };

const AuthCtx = React.createContext<AuthState>({ user: null, loading: true, setUser: () => {} });
export const useAuth = () => React.useContext(AuthCtx);

function Callback() {
  const nav = useNavigate();
  const { setUser } = useAuth();
  useEffect(() => {
    completeLogin()
      .then((user) => {
        setUser(user);
        nav('/');
      })
      .catch(() => nav('/'));
  }, [nav, setUser]);
  return <p>Signing in…</p>;
}

function Shell({ children }: { children: React.ReactNode }) {
  const { user, loading } = useAuth();
  return (
    <>
      <header>
        <Link to="/"><h1>Cron Agent</h1></Link>
        <nav>
          {user && (
            <>
              <span className="user">{user.profile.name ?? user.profile.sub}</span>
              <button onClick={logout}>Logout</button>
            </>
          )}
          {!user && !loading && <button onClick={login}>Login</button>}
        </nav>
      </header>
      <main>{children}</main>
    </>
  );
}

function App() {
  const [state, setState] = useState<AuthState>({ user: null, loading: true, setUser: () => {} });

  useEffect(() => {
    getUser().then((user) => setState((s) => ({ ...s, user, loading: false })));
  }, []);

  const setUser = (user: User | null) => setState((s) => ({ ...s, user }));

  return (
    <AuthCtx.Provider value={{ ...state, setUser }}>
      <BrowserRouter basename={basename}>
        <Shell>
          <Routes>
            <Route path="/" element={<JobsList />} />
            <Route path="/jobs/new" element={<JobEdit />} />
            <Route path="/jobs/:id" element={<JobEdit />} />
            <Route path="/jobs/:id/history" element={<JobHistory />} />
            <Route path="/callback" element={<Callback />} />
          </Routes>
        </Shell>
      </BrowserRouter>
    </AuthCtx.Provider>
  );
}

ReactDOM.createRoot(document.getElementById('root')!).render(<App />);
