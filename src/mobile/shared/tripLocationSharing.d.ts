export interface TripLocationSharingState {
  appState: string | null;
  mounted: boolean;
  status: string;
  locationSharingActive: boolean;
}

export interface DriverRideLifecycleState {
  userId?: string | null;
  rideId?: string | null;
  hasAccessToken?: boolean;
  sessionCurrent?: boolean;
  status?: string | null;
  locationSharingActive?: boolean | null;
}

export function shouldMaintainDriverRideLifecycle(state: DriverRideLifecycleState | null | undefined): boolean;

export function shouldWatchDriverTripLocation(state: DriverRideLifecycleState | null | undefined): boolean;

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
  status: string;
  locationSharingActive?: boolean | null;
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

export interface RiderLocationVisibilityState extends TripLocationSnapshot {
  status: string;
  locationSharingActive?: boolean | null;
}

export function shouldApplyTripLocationUpdate(
  current: RiderLocationVisibilityState,
  update: TripLocationUpdate
): boolean;

export function applyRiderRideStatusUpdate<T extends RiderLocationVisibilityState>(
  current: T,
  update: { rideId: string; status: string }
): T;

export function canShowRiderDriverLocation(
  ride: RiderLocationVisibilityState | null | undefined
): ride is RiderLocationVisibilityState & { driverLatitude: number; driverLongitude: number };

export function mergeTripLocationSnapshot<T extends TripLocationSnapshot & { driverLocationCapturedAtUtc?: string | null }>(
  current: TripLocationSnapshot,
  snapshot: T
): T & TripLocationSnapshot;

export function getRideMapFitCoordinates(
  ride: RiderLocationVisibilityState & { pickupLatitude?: number | null; pickupLongitude?: number | null }
): Array<{ latitude: number; longitude: number }>;

export function shouldFitRideMap(options: {
  tab: string;
  rideId?: string | null;
  readyRideId?: string | null;
  followDriverLocation: boolean;
}): boolean;
