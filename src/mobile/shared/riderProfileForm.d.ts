export type RiderThemeOption = "system" | "light" | "dark";
export type RiderProfileField = "helpQuestion";
export type RiderProfileAction = "askHelp" | "signOut";
export type RiderNavigationTab = "Book" | "Activity" | "Offers" | "Profile";
export type RiderHelpSource = "approved-content" | "provider-approved-content" | "deterministic-fallback";
export type RiderHelpSourceId =
  | "state-negotiating"
  | "state-agreement-confirmed"
  | "state-accepted"
  | "state-arrived"
  | "state-in-progress"
  | "state-completed"
  | "state-cancelled"
  | "state-expired"
  | "fare-negotiation"
  | "agreement-version"
  | "direct-payment"
  | "driver-photo"
  | "driver-selection"
  | "reservation"
  | "queue-status"
  | "availability-status"
  | "unsupported";

export interface RiderHelpResponse {
  answer: string;
  sourceId: RiderHelpSourceId;
  escalationRequired: boolean;
  source: RiderHelpSource;
}

export interface RiderThemeOptionProps {
  accessibilityRole: "radio";
  accessibilityLabel: string;
  accessibilityHint: string;
  accessibilityState: { checked: boolean };
}

export interface RiderProfileFieldProps {
  accessibilityLabel: string;
  accessibilityHint: string;
  accessibilityState: { disabled: boolean };
}

export interface RiderProfileActionProps {
  accessibilityRole: "button";
  accessibilityLabel: string;
  accessibilityHint: string;
  accessibilityState: { disabled: boolean; busy: boolean };
}

export interface RiderTabProps {
  accessibilityRole: "tab";
  accessibilityLabel: string;
  accessibilityHint: string;
  accessibilityState: { selected: boolean; disabled: boolean };
}

export function getRiderThemeOptionProps(option: RiderThemeOption, selected: boolean): RiderThemeOptionProps;
export function getRiderProfileFieldProps(field: RiderProfileField, disabled: boolean): RiderProfileFieldProps;
export function getRiderProfileActionProps(action: RiderProfileAction, disabled: boolean, busy: boolean): RiderProfileActionProps;
export function getRiderTabProps(tab: RiderNavigationTab, selected: boolean, disabled: boolean): RiderTabProps;

export function normalizeRiderHelpResponse(value: unknown): RiderHelpResponse | null;
export function getRiderHelpSourceLabel(source: RiderHelpSource): string;

export function shouldApplyRiderProfileRequestResponse(requestSequence: number, currentSequence: number, requestSessionEpoch: number, currentSessionEpoch: number): boolean;
