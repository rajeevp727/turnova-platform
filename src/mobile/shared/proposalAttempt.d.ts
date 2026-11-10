export type ProposalActor = "rider" | "driver";
export type ProposalAttempt = {
  fingerprint: string;
  key: string;
  rideId: string;
  actor: ProposalActor;
  amount: number;
  currency: string;
};

export type ProposalAttemptRef = { current: ProposalAttempt | null };

export function getOrCreateProposalAttempt(
  ref: ProposalAttemptRef,
  input: { rideId: string; actor: ProposalActor; amount: number; currency?: string },
  keyFactory?: (actor: ProposalActor) => string
): ProposalAttempt;

export function clearProposalAttempt(ref: ProposalAttemptRef, expectedKey: string): boolean;
