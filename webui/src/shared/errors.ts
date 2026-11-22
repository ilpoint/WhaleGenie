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
