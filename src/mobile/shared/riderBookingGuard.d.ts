export type RiderBookingStatus =
  | "Requested"
  | "Matching"
  | "Offered"
  | "Negotiating"
  | "AgreementConfirmed"
  | "Accepted"
  | "Arrived"
  | "InProgress"
  | "Completed"
  | "Cancelled"
  | "Expired";

export function hasActiveRiderRide(
  ride: { status: string } | null | undefined
): boolean;
