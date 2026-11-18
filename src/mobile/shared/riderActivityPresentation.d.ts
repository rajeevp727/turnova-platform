export interface RiderActivityRideStatus {
  status: string;
  negotiationExpiresAtUtc?: string | null;
}

export interface RiderActivityHistoryItem {
  rideId: string;
  pickup: string;
  dropoff: string;
  passengers: number;
  serviceClass: string;
  status: string;
  requestedAt: string;
}

export interface RiderActivityAgreementVersion {
  version: number;
  amount: number;
  currency: string;
  proposedBy: string;
  riderConfirmed: boolean;
  riderConfirmedAtUtc: string | null;
  driverConfirmed: boolean;
  driverConfirmedAtUtc: string | null;
}

export function getRiderRideStatusMessage(ride: RiderActivityRideStatus): string;
export function getRiderHistoryAccessibilityLabel(item: RiderActivityHistoryItem): string;
export function getRiderAgreementVersionAccessibilityLabel(item: RiderActivityAgreementVersion): string;
export function isRiderCancellationResponseValid(response: unknown, expectedRideId: string): boolean;
