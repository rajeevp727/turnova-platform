export type SessionStateSetters = {
  [key: string]: (value: any) => void;
};

export function resetRiderSessionState(setters: SessionStateSetters): void;
export function resetDriverSessionState(setters: SessionStateSetters): void;
