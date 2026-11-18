export function getSignalRReconnectDelayMs(previousRetryCount: number): number;

export interface SignalRStartableConnection {
  start(): Promise<void>;
}

export function startSignalRConnection(
  connection: SignalRStartableConnection,
  shouldContinue: () => boolean,
  onRetry?: () => void,
  waitForRetry?: (milliseconds: number) => Promise<void>
): Promise<boolean>;

export interface SignalRAccessTokenOptions {
  readAccessToken: () => Promise<string> | string;
  isCurrent: () => boolean;
  validateSession: () => Promise<unknown>;
  now?: () => number;
}

export function getJwtExpiryMilliseconds(accessToken: string): number | null;
export function getSignalRAccessToken(options: SignalRAccessTokenOptions): Promise<string>;
