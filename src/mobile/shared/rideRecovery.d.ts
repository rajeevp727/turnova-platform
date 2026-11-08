export type RideRecoveryRole = "rider" | "driver";

export function findActiveRide<T extends { status: string }>(
  history: readonly T[],
  role: RideRecoveryRole
): T | null;

export function shouldApplyRideStatusUpdate(
  currentRideId: string | null | undefined,
  updateRideId: string | null | undefined,
  isActive: boolean
): boolean;
