export type RideRecoveryRole = "rider" | "driver";

export interface RiderRideHistoryRecord {
  rideId: string;
  pickup: string;
  dropoff: string;
  passengers: number | null;
  serviceClass: string;
  status: string;
  requestedAt: string;
  acceptedAt: string | null;
  stops: string[];
}

export function normalizeRiderRideHistory(
  history: unknown
): readonly RiderRideHistoryRecord[] | null;

export function isRiderRideHistoryShapeValid(
  history: unknown
): boolean;

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
