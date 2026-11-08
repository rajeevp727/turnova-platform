export interface TripLocationSharingState {
  appState: string | null;
  mounted: boolean;
  status: string;
  locationSharingActive: boolean;
}

export function canShareTripLocation(state: TripLocationSharingState): boolean;

export interface AuthoritativeTripLocationState {
  rideId: string;
  status: string;
  locationSharingActive: boolean;
}

export function findAuthoritativeActiveTrip<T extends AuthoritativeTripLocationState>(
  history: T[],
  rideId: string
): T | null;

export interface TripLocationSnapshot {
  rideId: string;
  driverLatitude?: number | null;
  driverLongitude?: number | null;
  locationCapturedAtUtc?: string | null;
  driverLocationCapturedAtUtc?: string | null;
  locationUpdatedAt?: string | null;
}

export interface TripLocationUpdate {
  rideId: string;
  latitude: number;
  longitude: number;
  locationCapturedAtUtc?: string | null;
  locationUpdatedAt?: string | null;
}

export function shouldApplyTripLocationUpdate(
  current: TripLocationSnapshot,
  update: TripLocationUpdate
): boolean;

export function getRideMapFitCoordinates(
  ride: TripLocationSnapshot & { pickupLatitude?: number | null; pickupLongitude?: number | null }
): Array<{ latitude: number; longitude: number }>;
