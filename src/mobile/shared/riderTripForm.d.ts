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
export function getRiderDriverOptionProps(driver: RiderDriverOption, selected: boolean, disabled: boolean, unionName?: string): RiderDriverOptionProps;
