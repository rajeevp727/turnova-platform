export type RideRecoveryRole = "rider" | "driver";

export function findActiveRide<T extends { status: string }>(
  history: readonly T[],
  role: RideRecoveryRole
): T | null;

export function isTerminalRideStatus(status: string): boolean;
