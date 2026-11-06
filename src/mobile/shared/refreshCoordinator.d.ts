export interface InFlightPromiseRef<T> {
  current: Promise<T> | null;
}

export function singleFlight<T>(
  ref: InFlightPromiseRef<T>,
  operation: () => Promise<T>
): Promise<T>;
