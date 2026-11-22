import { describe, expect, it } from 'vitest';
import { describeError } from '../../src/shared/errors.js';

describe('describeError', () => {
  it('keeps a string failure as-is', () => {
    expect(describeError('bad input')).toBe('bad input');
  });

  it('reads the message of an error object', () => {
    expect(describeError(new Error('host said no'))).toBe('host said no');
  });

  it('stringifies anything else', () => {
    expect(describeError(42)).toBe('42');
    expect(describeError(null)).toBe('null');
    expect(describeError(undefined)).toBe('undefined');
  });
});
