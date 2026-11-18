export type RiderBookingStatus =
  | "Requested"
  | "Matching"
  | "Offered"
  | "Negotiating"
  | "AgreementConfirmed"
  | "Accepted"
  | "Arrived"
  | "InProgress"
  | "Completed"
  | "Cancelled"
  | "Expired";

export interface RiderSelectedBookingExpected {
  riderId: string;
  driverId: string;
  unionId: string;
  pickup: string;
  dropoff: string;
  stops: string[];
  passengers: number;
  pickupLatitude: number;
  pickupLongitude: number;
}

export interface RiderSelectedBookingResult {
  rideId: string;
  pickup: string;
  dropoff: string;
  passengers: number;
  serviceClass: "cab";
  status: string;
  requestedAt: string;
  acceptedAt: string | null;
  estimatedFareInr: null;
  stops: string[];
  unionId: string;
  unionName: string;
  driverDisplayName: string;
  driverVehicleMake: string;
  driverVehicleModel: string;
  driverDistanceToPickupKm: number;
  negotiationExpiresAtUtc: string | null;
  pickupLatitude: number;
  pickupLongitude: number;
  driverLatitude?: number;
  driverLongitude?: number;
  locationCapturedAtUtc: string | null;
  driverLocationCapturedAtUtc: string | null;
  locationUpdatedAt?: string;
}

export function normalizeRiderSelectedBookingResponse(
  response: unknown,
  expected: RiderSelectedBookingExpected
): RiderSelectedBookingResult | null;
export function hasActiveRiderRide(
  ride: { status: string } | null | undefined
): boolean;
export function canStartRiderBooking(
  rideStateLoaded: boolean,
  ride: { status: string } | null | undefined
): boolean;
