import { UserManager, User as OidcUser, WebStorageStateStore } from 'oidc-client-ts';

export type User = OidcUser;

export const AUTHORITY = import.meta.env.VITE_AR_AUTH_AUTHORITY ?? 'https://auth.adolfrey.com/api';
export const CLIENT_ID = import.meta.env.VITE_AR_AUTH_CLIENT_ID ?? 'PLACEHOLDER_CLIENT_ID';
export const SCOPE = import.meta.env.VITE_AR_AUTH_SCOPE ?? 'openid profile email';
export const PROTOTYPE = import.meta.env.VITE_AR_AUTH_PROTOTYPE === 'true';

const userManager = new UserManager({
  authority: AUTHORITY,
  client_id: CLIENT_ID,
  redirect_uri:import.meta.env.VITE_AR_AUTH_REDIRECT_URI ?? 'PLACEHOLDER_REDIRECT_URI',
  response_type: 'code',
  scope: SCOPE,
  userStore: new WebStorageStateStore({ store: window.sessionStorage }),
  monitorSession: false,
  extraQueryParams: PROTOTYPE ? { prototype: 'true' } : undefined,
});

export interface Auth { user: User | null }

export async function getUser(): Promise<User | null> {
  return await userManager.getUser();
}

export async function login(): Promise<void> {
  await userManager.signinRedirect();
}

export async function completeLogin(): Promise<User> {
  return await userManager.signinRedirectCallback();
}

export async function logout(): Promise<void> {
  await userManager.signoutRedirect();
}

export async function apiFetch(path: string, user: User | null, init?: RequestInit): Promise<Response> {
  const headers = new Headers(init?.headers);
  if (user?.access_token) headers.set('Authorization', `Bearer ${user.access_token}`);
  if (init?.body) headers.set('Content-Type', 'application/json');
  const resp = await fetch(path, { ...init, headers });
  if (resp.status === 401) {
    await login();
  }
  return resp;
}
