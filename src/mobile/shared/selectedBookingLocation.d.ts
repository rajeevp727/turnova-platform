import type { PickupCoordinates } from "./foregroundPickupLocation";

export interface SelectedBookingLocation {
  capture(): Promise<PickupCoordinates>;
  reset(): void;
}

export function createSelectedBookingLocation(
  location: typeof import("expo-location")
): SelectedBookingLocation;
