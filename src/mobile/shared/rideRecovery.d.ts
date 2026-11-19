export type RideRecoveryRole = "rider" | "driver";

export function isRiderRideHistoryShapeValid(
  history: unknown
): history is readonly { rideId: string; status?: unknown }[];

export function hasUnresolvedRiderRideHistory(
  history: unknown
): boolean;

export function isRiderRideDetailsResponseValid(
  response: unknown,
  expectedRideId: string
): boolean;

export function shouldApplyRideHistoryResponse(
  requestSequence: number,
  currentSequence: number
): boolean;

export function findActiveRide<T extends { status: string }>(
  history: readonly T[],
  role: RideRecoveryRole
): T | null;

export function applyRideStatusUpdate<T extends { rideId: string; status: string }>(
  ride: T,
  update: { rideId: string; status: string }
): T;

export function applyRideStatusUpdate<T extends { rideId: string; status: string }>(
  ride: T | null | undefined,
  update: { rideId: string; status: string }
): T | null | undefined;

export function shouldApplyRideStatusUpdate(
  currentRideId: string | null | undefined,
  updateRideId: string | null | undefined,
  isActive: boolean
): boolean;
