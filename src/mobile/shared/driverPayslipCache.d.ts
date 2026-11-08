export type DriverPayslipFileSystem = {
  cacheDirectory: string | null;
  EncodingType: { Base64: string };
  readDirectoryAsync(uri: string): Promise<string[]>;
  deleteAsync(uri: string, options?: { idempotent?: boolean }): Promise<void>;
  writeAsStringAsync(uri: string, contents: string, options?: { encoding?: string }): Promise<void>;
};

export function isManagedDriverPayslipUri(fileSystem: DriverPayslipFileSystem, uri: string): boolean;
export function deleteManagedDriverPayslip(fileSystem: DriverPayslipFileSystem, uri: string): Promise<boolean>;
export function clearCachedDriverPayslips(fileSystem: DriverPayslipFileSystem): Promise<{ removed: number; failed: number }>;
export function writeCachedDriverPayslipIfCurrent(
  fileSystem: DriverPayslipFileSystem,
  uri: string,
  base64: string,
  isSessionCurrent: () => boolean
): Promise<boolean>;
