export type RiderAuthMode = "login" | "register";
export type RiderAuthField = "name" | "email" | "password";
export interface MutableBooleanRef { current: boolean; }
export function tryBeginRiderAuthSubmission(gate: MutableBooleanRef): boolean;
export function finishRiderAuthSubmission(gate: MutableBooleanRef): void;

export interface RiderAuthFieldProps {
  accessibilityLabel: string;
  accessibilityHint: string;
  autoCapitalize: "none" | "words";
  autoCorrect: boolean;
  autoComplete: "name" | "email" | "current-password" | "new-password";
  textContentType: "name" | "emailAddress" | "password" | "newPassword";
  importantForAutofill: "yes";
  returnKeyType: "next" | "go";
  blurOnSubmit?: boolean;
  keyboardType?: "email-address";
  secureTextEntry?: boolean;
}

export interface RiderAuthActionProps {
  accessibilityRole: "button";
  accessibilityLabel: string;
  accessibilityHint: string;
  accessibilityState: { disabled: boolean; busy: boolean };
}

export function getRiderAuthFieldProps(field: RiderAuthField, mode?: RiderAuthMode): RiderAuthFieldProps;
export function getRiderAuthSubmitProps(mode: RiderAuthMode, disabled: boolean, busy: boolean): RiderAuthActionProps;
export function getRiderAuthModeToggleProps(mode: RiderAuthMode): {
  accessibilityRole: "button";
  accessibilityLabel: string;
  accessibilityHint: string;
};
export function getRiderAuthErrorProps(): {
  accessible: true;
  accessibilityRole: "alert";
  accessibilityLiveRegion: "assertive";
};
