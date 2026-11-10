export type RiderBookingStatus =
  | "Requested"
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
