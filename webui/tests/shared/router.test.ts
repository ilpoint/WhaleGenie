import { beforeEach, describe, expect, it } from 'vitest';
import { currentRoute, go, setNavigationGuard } from '../../src/shared/navigation/router.js';

beforeEach(() => {
  window.location.hash = '';
  setNavigationGuard(() => true);
});

describe('shell routing', () => {
  it('lands on the macro list when the address names no page', () => {
    expect(currentRoute()).toBe('macros');
    window.location.hash = 'editor';
    expect(currentRoute()).toBe('editor');
  });

  it('treats the old home address as the macro list', () => {
    window.location.hash = 'home';
    expect(currentRoute()).toBe('macros');
  });

  it('lets the guard keep the page where it is', async () => {
    setNavigationGuard(() => false);
    window.location.hash = 'macros';

    await go('settings');

    expect(currentRoute()).toBe('macros');
  });

  it('hands the target to the guard and changes the address when it agrees', async () => {
    const seen: string[] = [];
    setNavigationGuard((to) => {
      seen.push(to);
      return true;
    });

    await go('settings');

    expect(seen).toEqual(['settings']);
    expect(currentRoute()).toBe('settings');
  });
});
