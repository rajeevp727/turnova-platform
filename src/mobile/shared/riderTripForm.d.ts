export type RiderTripField = "tripIntent" | "pickup" | "dropoff" | "stops" | "passengers";
export type RiderTripAction = "draft" | "discover" | "requestDriver";

export interface RiderTripFieldProps {
  accessibilityLabel: string;
  accessibilityHint: string;
  accessibilityState: { disabled: boolean };
}

export interface RiderTripActionProps {
  accessibilityRole: "button";
  accessibilityLabel: string;
  accessibilityHint: string;
  accessibilityState: { disabled: boolean; busy: boolean };
}

export interface RiderDriverOption {
  fullName: string;
  vehicleMake: string;
  vehicleModel: string;
  passengerCapacity: number;
  distanceToPickupKm: number;
}

export interface RiderDriverOptionProps {
  accessibilityRole: "radio";
  accessibilityLabel: string;
  accessibilityHint: string;
  accessibilityState: { checked: boolean; disabled: boolean };
}

export function getRiderTripFieldProps(field: RiderTripField, disabled: boolean): RiderTripFieldProps;
export function getRiderTripActionProps(action: RiderTripAction, disabled: boolean, busy: boolean): RiderTripActionProps;
export interface RiderDiscoveryDriver {
  driverId: string;
  fullName: string;
  vehicleMake: string;
  vehicleModel: string;
  serviceClass: "cab";
  passengerCapacity: number;
  distanceToPickupKm: number;
  profilePhotoBase64: string | null;
  profilePhotoContentType: string | null;
}
export interface RiderDiscoveryUnion {
  unionId: string;
  unionName: string;
  drivers: RiderDiscoveryDriver[];
}
export interface RiderDiscoveryResponse {
  pickupZoneId: string;
  journeyDistanceKm: number | null;
  unions: RiderDiscoveryUnion[];
}
export function canDiscoverRiderTrip(pickup: unknown, dropoff: unknown, passengerCount: number | null): boolean;
export function validateRiderBookingText(pickup: unknown, dropoff: unknown, stopsText: unknown): string | null;
export function shouldApplyRiderDiscoveryResponse(requestSequence: number, currentSequence: number): boolean;
export function normalizeRiderDiscoveryResponse(response: unknown): RiderDiscoveryResponse | null;

export function getVisibleUnionDrivers<T>(drivers: readonly T[] | null | undefined): T[];

export function getRiderDriverOptionProps(driver: RiderDriverOption, selected: boolean, disabled: boolean, unionName?: string): RiderDriverOptionProps;
