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
