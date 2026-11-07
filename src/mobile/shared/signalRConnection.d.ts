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
