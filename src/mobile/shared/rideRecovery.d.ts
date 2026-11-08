export type RideRecoveryRole = "rider" | "driver";

export function findActiveRide<T extends { status: string }>(
  history: readonly T[],
  role: RideRecoveryRole
): T | null;

export function applyRideStatusUpdate<T extends { rideId: string; status: string }>(
  ride: T | null | undefined,
  update: { rideId: string; status: string }
): T | null | undefined;
