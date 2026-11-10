export function readJsonResponseIfCurrent<T extends Record<string, unknown> = Record<string, unknown>>(
  response: { json: () => Promise<T> },
  isCurrent: () => boolean
): Promise<T>;
