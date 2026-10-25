import axios from 'axios';
import { API_BASE, getUser, login } from '../oidc';

// Axios instance rooted at {APP_ROOT}/api (e.g. "/cron-agent/api").
// The bearer token is attached per request from the OIDC session; a 401
// triggers the OIDC login redirect.
export const api = axios.create({ baseURL: API_BASE });

api.interceptors.request.use(async (config) => {
  const user = await getUser();
  if (user?.access_token) config.headers.Authorization = `Bearer ${user.access_token}`;
  return config;
});

api.interceptors.response.use(
  (resp) => resp,
  async (error) => {
    if (axios.isAxiosError(error) && error.response?.status === 401) await login();
    return Promise.reject(error);
  },
);

// HTTP status (or "network error") of a failed request, for error banners.
export function errStatus(err: unknown): string {
  if (axios.isAxiosError(err)) return String(err.response?.status ?? 'network error');
  return 'unknown error';
}

// Response body of a failed request, e.g. RFC 7807 problem details.
export function errData(err: unknown): unknown {
  if (axios.isAxiosError(err)) return err.response?.data ?? null;
  return null;
}
