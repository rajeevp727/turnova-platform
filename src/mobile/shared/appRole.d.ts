export type TurnOvaAppRole = "Rider" | "Driver";

export interface AppUserWithRole {
  role: string;
}

export function requireAppRole<T extends AppUserWithRole>(
  user: T | null | undefined,
  expectedRole: TurnOvaAppRole
): T;
