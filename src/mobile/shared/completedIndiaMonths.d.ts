export interface CompletedIndiaMonth {
  year: number;
  month: number;
}

/** Returns the most recent completed India-local calendar months, newest first. */
export function getCompletedIndiaMonths(now?: Date, count?: number): CompletedIndiaMonth[];

/** Formats a month label using the India time zone and the requested device locale. */
export function formatIndiaMonthLabel(year: number, month: number, locale?: string | string[]): string;
