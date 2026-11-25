/** The stable code on a bridge rejection, or null for anything that is not one. */
export function bridgeErrorCode(error: unknown): string | null {
  if (!error || typeof error !== 'object' || !('code' in error)) {
    return null;
  }

  const code = (error as { code: unknown }).code;
  return typeof code === 'string' ? code : null;
}

/** Turns an unknown command failure into the text a page shows. */
export function describeError(error: unknown): string {
  if (typeof error === 'string') {
    return error;
  }

  if (error && typeof error === 'object' && 'message' in error) {
    const message = (error as { message: unknown }).message;
    return typeof message === 'string' ? message : String(message);
  }

  return String(error);
}
