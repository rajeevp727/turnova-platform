export interface PickupCoordinates {
  latitude: number;
  longitude: number;
}

export function getForegroundPickupCoordinates(
  location: typeof import("expo-location")
): Promise<PickupCoordinates>;

export function discoverNearbyCabDrivers<T>(
  location: typeof import("expo-location"),
  request: (path: string) => Promise<T>,
  passengers: number
): Promise<{ coordinates: PickupCoordinates; discovery: T }>;
