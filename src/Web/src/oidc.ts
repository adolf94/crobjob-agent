import { UserManager, User as OidcUser, WebStorageStateStore } from 'oidc-client-ts';

export type User = OidcUser;

export const AUTHORITY = import.meta.env.VITE_AR_AUTH_AUTHORITY ?? 'https://auth.adolfrey.com/api';
export const CLIENT_ID = import.meta.env.VITE_AR_AUTH_CLIENT_ID ?? 'PLACEHOLDER_CLIENT_ID';
export const SCOPE = import.meta.env.VITE_AR_AUTH_SCOPE ?? 'openid profile email';
export const PROTOTYPE = import.meta.env.VITE_AR_AUTH_PROTOTYPE === 'true';

// Vite sets BASE_URL from its `base` config (APP_ROOT in CI/Docker), e.g.
// "/cron-agent/". API endpoints live under the app root + "api".
export const BASE_URL = import.meta.env.BASE_URL.replace(/\/+$/, '') || '';
export const API_BASE = `${BASE_URL}/api`;

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
