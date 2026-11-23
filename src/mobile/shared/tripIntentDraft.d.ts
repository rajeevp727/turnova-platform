export interface RiderTripIntentDraft {
  pickup: string | null;
  dropoff: string | null;
  serviceClass: "Cab";
  passengerCount: number | null;
  needsClarification: boolean;
  missingFields: string[];
  requiresUserConfirmation: true;
  source: "provider" | "deterministic-fallback";
}

export function passengerCountInputFromDraft(value: number | null | undefined): string;
export function parsePassengerCountInput(value: string): number | null;
export function normalizeRiderTripIntentDraft(value: unknown): RiderTripIntentDraft | null;
export function ownsRiderTripIntentRequest(
  requestSequence: number,
  currentSequence: number,
  requestSessionEpoch: number,
  currentSessionEpoch: number
): boolean;
export function shouldApplyRiderTripIntentResponse(
  requestSequence: number,
  currentSequence: number,
  requestSessionEpoch: number,
  currentSessionEpoch: number,
  requestFormRevision: number,
  currentFormRevision: number
): boolean;
