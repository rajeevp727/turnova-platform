export type RiderThemeOption = "system" | "light" | "dark";
export type RiderProfileField = "helpQuestion";
export type RiderProfileAction = "askHelp" | "signOut";
export type RiderNavigationTab = "Book" | "Activity" | "Offers" | "Profile";

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

export function shouldApplyRiderProfileRequestResponse(requestSequence: number, currentSequence: number, requestSessionEpoch: number, currentSessionEpoch: number): boolean;
