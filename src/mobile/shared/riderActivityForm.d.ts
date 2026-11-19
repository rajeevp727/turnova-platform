export function parseRiderAgreementAmount(value: string): number | null;
export type RiderActivityField = "agreementAmount";
export type RiderActivityAction = "propose" | "confirm" | "cancel" | "followDriver";

export interface RiderActivityFieldProps {
  accessibilityLabel: string;
  accessibilityHint: string;
  accessibilityState: { disabled: boolean };
}

export interface RiderActivityActionProps {
  accessibilityRole: "button";
  accessibilityLabel: string;
  accessibilityHint: string;
  accessibilityState: { disabled: boolean; busy: boolean };
}

export interface RiderActivityActionLock { current: string | null; }

export function tryBeginRiderActivityAction(inFlight: RiderActivityActionLock, key: string): boolean;
export function finishRiderActivityAction(inFlight: RiderActivityActionLock, key: string): boolean;
export function getRiderActivityFieldProps(field: RiderActivityField, disabled: boolean): RiderActivityFieldProps;
export function getRiderActivityActionProps(action: RiderActivityAction, disabled: boolean, busy: boolean, confirmed?: boolean): RiderActivityActionProps;
