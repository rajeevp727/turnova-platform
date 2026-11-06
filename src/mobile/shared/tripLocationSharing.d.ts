export interface TripLocationSharingState {
  appState: string | null;
  mounted: boolean;
  status: string;
  locationSharingActive: boolean;
}

export function canShareTripLocation(state: TripLocationSharingState): boolean;
