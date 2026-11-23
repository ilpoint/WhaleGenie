// @vitest-environment jsdom
import { describe, expect, it } from 'vitest';
import { aimMarkup } from '../../../src/features/overlay/wheel.js';
import { demoSurfaceWheel } from '../../../src/shared/bridge/mock-fixtures.js';

describe('wheel aim layer', () => {
  it('draws the guide from the pointer even while no sector is aimed', () => {
    const markup = aimMarkup(demoSurfaceWheel, null, { x: 300, y: 10 });

    expect(markup).toContain('<line class="guide"');
    expect(markup).toContain('x2="720.00"');
    expect(markup).toContain('y2="430.00"');
    expect(markup).not.toContain('class="sector aimed');
  });

  it('draws the aimed sector independently of the guide', () => {
    const markup = aimMarkup(demoSurfaceWheel,
      { ring: 'inner', index: 0, x: 200, y: 0 }, { x: 200, y: 0 });

    expect(markup).toContain('class="sector aimed');
    expect(markup).toContain('<line class="guide"');
  });

  it('leaves an empty aim layer when there is no pointer', () => {
    expect(aimMarkup({ ...demoSurfaceWheel, centerName: false }, null, null)).toBe('');
  });
});