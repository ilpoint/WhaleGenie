/** library bridge shapes. The barrel protocol.ts re-exports this file. */

export interface LibraryEntry {
  id: string;
  name: string;
  version: number;
  description: string;
}

export interface LibraryState {
  entries: LibraryEntry[];
  error: string | null;
}
