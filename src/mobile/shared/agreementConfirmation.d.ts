export type AgreementParty = "Rider" | "Driver";

export function agreementConfirmationLabel(
  party: AgreementParty,
  confirmed: boolean,
  confirmedAtUtc: string | null | undefined
): string;
