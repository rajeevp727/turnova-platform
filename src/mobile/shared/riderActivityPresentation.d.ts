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
  rideId: string;
  version: number;
  amount: number;
  currency: string;
  pickup: string;
  dropoff: string;
  stops: string[];
  createdAtUtc: string;
  proposedBy: "Rider" | "Driver";
  riderConfirmed: boolean;
  riderConfirmedAtUtc: string | null;
  driverConfirmed: boolean;
  driverConfirmedAtUtc: string | null;
}
export function getRiderRideStatusLabel(status: unknown): string;
export function isRiderRideStatusEventValid(update: unknown, expectedRideId: string): boolean;
export function getRiderRideStatusMessage(ride: RiderActivityRideStatus): string;
export function getRiderHistoryAccessibilityLabel(item: RiderActivityHistoryItem): string;
export function getRiderAgreementVersionRouteLabel(item: RiderActivityAgreementVersion): string;
export function getRiderAgreementVersionCreatedAtLabel(item: RiderActivityAgreementVersion): string;
export function getRiderAgreementVersionAccessibilityLabel(item: RiderActivityAgreementVersion): string;
export function isRiderCancellationResponseValid(response: unknown, expectedRideId: string): boolean;
export function normalizeRiderAgreementHistory(value: unknown, expectedRideId: string): RiderActivityAgreementVersion[] | null;
export function shouldApplyRiderAgreementHistoryResponse(requestSequence: number, currentSequence: number): boolean;
