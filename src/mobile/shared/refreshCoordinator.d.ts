export interface InFlightPromiseRef<T> {
  current: Promise<T> | null;
}

export function singleFlight<T>(
  ref: InFlightPromiseRef<T>,
  operation: () => Promise<T>
): Promise<T>;

export interface StoredSessionTokens {
  accessToken: string | null;
  refreshToken: string | null;
}

export function getFreshAccessToken(
  ref: InFlightPromiseRef<string>,
  rejectedAccessToken: string,
  readTokens: () => Promise<StoredSessionTokens | null>,
  refresh: (refreshToken: string) => Promise<string>
): Promise<string>;

export interface SessionEpochRef {
  current: number;
}

export function advanceSessionEpoch(
  epochRef: SessionEpochRef,
  inFlightRefreshRef?: InFlightPromiseRef<string>
): number;

export interface AuthenticatedSessionEpochRef {
  current: number | null;
}

export function isAuthenticatedSessionCurrent(
  sessionEpochRef: SessionEpochRef,
  authenticatedEpochRef: AuthenticatedSessionEpochRef
): boolean;
