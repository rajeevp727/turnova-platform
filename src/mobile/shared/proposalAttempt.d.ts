export type ProposalActor = "rider" | "driver";
export type ProposalAttempt = {
  fingerprint: string;
  key: string;
  rideId: string;
  actor: ProposalActor;
  userId: string | null;
  amount: number;
  currency: string;
};
export type PersistentProposalAttempt = ProposalAttempt & { userId: string };

export type ProposalAttemptRef = { current: ProposalAttempt | null };
export type ProposalSecureStore = {
  getItemAsync(key: string): Promise<string | null>;
  setItemAsync(key: string, value: string): Promise<void>;
  deleteItemAsync(key: string): Promise<void>;
};

export function createProposalAttemptStorageKey(input: {
  userId: string; rideId: string; actor: ProposalActor; amount: number; currency?: string;
}): string;

export function getOrCreateProposalAttempt(
  ref: ProposalAttemptRef,
  input: { rideId: string; actor: ProposalActor; userId?: string | null; amount: number; currency?: string },
  keyFactory?: (actor: ProposalActor) => string
): ProposalAttempt;

export function getOrCreatePersistentProposalAttempt(
  storage: ProposalSecureStore,
  ref: ProposalAttemptRef,
  input: { userId: string; rideId: string; actor: ProposalActor; amount: number; currency?: string },
  keyFactory?: (actor: ProposalActor) => string,
  isCurrent?: () => boolean
): Promise<PersistentProposalAttempt>;

export function clearProposalAttempt(ref: ProposalAttemptRef, expectedKey: string): boolean;
export function clearPersistentProposalAttempt(
  storage: ProposalSecureStore,
  ref: ProposalAttemptRef,
  expectedAttempt: PersistentProposalAttempt
): Promise<boolean>;

export type ProposalSubmissionRef = { current: string | null };
export function finishProposalSubmission(ref: ProposalSubmissionRef, expectedKey: string): boolean;
