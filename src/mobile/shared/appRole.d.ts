export type TurnOvaAppRole = "Rider" | "Driver";

export interface AppUserWithRole {
  role: string;
}

export function requireAppRole<T extends AppUserWithRole = AppUserWithRole>(
  user: unknown,
  expectedRole: TurnOvaAppRole
): T;
