/** mcp bridge shapes. The barrel protocol.ts re-exports this file. */

export interface McpState {
  allowControl: boolean;
  allowRun: boolean;
  config: string;
  auditFile: string;
}
