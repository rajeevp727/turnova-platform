export type RiderSupportRequestField = "subject" | "description";

export interface RiderSupportRequestAttempt {
  idempotencyKey: string;
  subject: string;
  description: string;
}

export interface RiderSupportRequestResponse {
  requestId: string;
  submittedAtUtc: string;
  replayed: boolean;
}
export interface RiderSupportRequestApiInit {
  method: "POST";
  headers: { "Idempotency-Key": string };
  body: string;
}
export type RiderSupportRequestApiFetch = (path: string, init: RiderSupportRequestApiInit) => Promise<unknown>;

export interface RiderSupportFieldProps {
  accessibilityLabel: string;
  accessibilityHint: string;
  accessibilityState: { disabled: boolean };
}

export interface RiderSupportActionProps {
  accessibilityRole: "button";
  accessibilityLabel: string;
  accessibilityHint: string;
  accessibilityState: { disabled: boolean; busy: boolean };
}

export function validateRiderSupportRequest(subject: string, description: string): string | null;
export function getRiderSupportRequestAttempt(
  currentAttempt: RiderSupportRequestAttempt | null,
  subject: string,
  description: string,
  createKey?: () => string
): RiderSupportRequestAttempt;
export function getRiderSupportFieldProps(field: RiderSupportRequestField, disabled: boolean): RiderSupportFieldProps;
export function getRiderSupportActionProps(disabled: boolean, busy: boolean): RiderSupportActionProps;

export function parseRiderSupportRequestResponse(value: unknown): RiderSupportRequestResponse;
export function submitRiderSupportRequest(apiFetch: RiderSupportRequestApiFetch, attempt: RiderSupportRequestAttempt): Promise<RiderSupportRequestResponse>;
