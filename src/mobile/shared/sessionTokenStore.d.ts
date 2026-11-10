export interface SessionTokenStorage {
  getItemAsync(key: string): Promise<string | null>;
  setItemAsync(key: string, value: string): Promise<void>;
  deleteItemAsync(key: string): Promise<void>;
}

export interface StoredSessionTokens {
  accessToken: string;
  refreshToken: string;
}

export interface RestoredSession {
  tokens: StoredSessionTokens;
  user: unknown;
}

export const ACCESS_TOKEN_KEY: "turnova.access";
export const REFRESH_TOKEN_KEY: "turnova.refresh";

export function readSessionTokens(
  storage: SessionTokenStorage,
  isCurrent?: () => boolean
): Promise<StoredSessionTokens | null>;

export function readAccessTokenIfCurrent(
  storage: SessionTokenStorage,
  fallbackToken: string,
  isCurrent: () => boolean
): Promise<string>;

export function saveSessionTokens(
  storage: SessionTokenStorage,
  tokens: StoredSessionTokens
): Promise<void>;

export function saveSessionTokensIfCurrent(
  storage: SessionTokenStorage,
  tokens: StoredSessionTokens,
  isCurrent: () => boolean
): Promise<boolean>;

export function clearSessionTokens(storage: SessionTokenStorage): Promise<void>;

export function logoutSession(
  storage: SessionTokenStorage,
  revoke: () => Promise<unknown>
): Promise<void>;

export function restoreStoredSession(
  storage: SessionTokenStorage,
  request: (path: string, init?: any, token?: string) => Promise<any>,
  isCurrent?: () => boolean
): Promise<RestoredSession | null>;
