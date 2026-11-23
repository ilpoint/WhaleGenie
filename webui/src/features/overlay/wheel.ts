import { LitElement, css, html } from 'lit';
import { customElement, state } from 'lit/decorators.js';
import { unsafeSVG } from 'lit/directives/unsafe-svg.js';
import { bridgeApi } from '../../shared/bridge/bridge.js';
import type { SurfaceWheelSnapshot } from '../../shared/bridge/protocol.js';
import { strings } from '../../shared/strings/strings.js';
import { StoreController } from '../../shared/lit/store-controller.js';

interface Annulus {
  from: number;
  to: number;
  count: number;
}

/** The radii of the rings a wheel draws, in the wheel's own coordinates. */
function rings(snapshot: SurfaceWheelSnapshot): Annulus[] {
  const half = snapshot.diameter / 2;
  if (snapshot.rings <= 1) {
    return [{ from: half * 0.30, to: half * 0.94, count: 6 }];
  }

  const inner: Annulus = { from: half * (snapshot.rings === 3 ? 0.16 : 0.22), to: half * (snapshot.rings === 3 ? 0.40 : 0.56), count: 8 };
  const outer: Annulus = { from: inner.to, to: half * (snapshot.rings === 3 ? 0.66 : 0.94), count: 8 };
  return snapshot.rings === 3
    ? [inner, outer, { from: outer.to, to: half * 0.94, count: Math.max(1, snapshot.thirdCapacity) }]
    : [inner, outer];
}

function point(centre: number, radius: number, angle: number): [number, number] {
  return [centre + radius * Math.sin(angle), centre - radius * Math.cos(angle)];
}

function sectorPath(centre: number, from: number, to: number, index: number, count: number): string {
  const step = (Math.PI * 2) / count;
  const start = angle(index - 0.5, step);
  const end = angle(index + 0.5, step);
  const segments = 16;
  const points: string[] = [];
  for (let stepIndex = 0; stepIndex <= segments; stepIndex += 1) {
    const at = start + (end - start) * (stepIndex / segments);
    const [x, y] = point(centre, to, at);
    points.push(`${x.toFixed(2)} ${y.toFixed(2)}`);
  }

  for (let stepIndex = segments; stepIndex >= 0; stepIndex -= 1) {
    const at = start + (end - start) * (stepIndex / segments);
    const [x, y] = point(centre, from, at);
    points.push(`${x.toFixed(2)} ${y.toFixed(2)}`);
  }

  return `M ${points.join(' L ')} Z`;
}

function angle(index: number, step: number): number {
  return (index - 0.5) * step;
}

/**
 * 轮盘浮层：C# 说“这一圈画几格、每格是什么”，页面只负责把扇区画出来。指针落点、扇区命中与
 * 重复/取消的判定留在 C#，所以换 DPI 或换主题不会改变“指针落在哪一格”。
 */
@customElement('wg-overlay-wheel')
export class WgOverlayWheel extends LitElement {
  @state()
  private snapshot: SurfaceWheelSnapshot | null = null;

  @state()
  private error = '';

  protected readonly stringsController = new StoreController(this, strings);

  static styles = css`
    :host {
      display: block;
      width: max-content;
      background: transparent;
      font-family: var(--fontFamilyBase);
      color: var(--colorNeutralForeground1);
      user-select: none;
    }

    svg {
      display: block;
      overflow: visible;
    }

    .sector {
      fill: var(--colorNeutralBackground1);
      stroke: var(--colorNeutralStroke2);
      stroke-width: 1;
    }

    .sector:hover {
      fill: var(--colorNeutralBackground3Hover);
    }

    .sector.aimed {
      fill: var(--colorBrandBackground2);
    }

    .label {
      font-size: 12px;
      fill: var(--colorNeutralForeground1);
      text-anchor: middle;
      dominant-baseline: middle;
      pointer-events: none;
    }

    .centre {
      fill: var(--colorNeutralBackground2);
      stroke: var(--colorNeutralStroke2);
    }

    .scene {
      font-size: 12px;
      fill: var(--colorNeutralForeground3);
      text-anchor: middle;
      dominant-baseline: middle;
    }

    .note {
      padding: 10px;
      font-size: 12px;
      color: var(--colorNeutralForeground3);
    }
  `;

  protected override async firstUpdated(): Promise<void> {
    await this.reload();
    window.addEventListener('focus', () => void this.reload());
    // The aim arrives as whole frames: the pointer writes many positions, the page keeps the last.
    bridgeApi.on('surface.wheel.changed', (next) => {
      this.snapshot = next;
    });
  }

  private async reload(): Promise<void> {
    try {
      this.snapshot = await bridgeApi.call<SurfaceWheelSnapshot>('surface.wheel.state');
      this.error = '';
    } catch (failure) {
      this.error = failure instanceof Error ? failure.message : String(failure);
    }
  }

  private svgMarkup(snapshot: SurfaceWheelSnapshot): string {
    const centre = snapshot.diameter / 2;
    const annuli = rings(snapshot);
    const parts: string[] = [];
    for (const slot of snapshot.slots) {
      const ring = slot.ring === 'inner' ? 0 : slot.ring === 'outer' ? 1 : 2;
      const annulus = annuli[ring];
      if (!annulus) {
        continue;
      }

      const middle = (annulus.from + annulus.to) / 2;
      const step = (Math.PI * 2) / annulus.count;
      const [x, y] = point(centre, middle, slot.index * step);
      const aimed = snapshot.aim?.ring === slot.ring && snapshot.aim.index === slot.index;
      parts.push(`<path class="sector${aimed ? ' aimed' : ''}" d="${sectorPath(centre, annulus.from, annulus.to, slot.index, annulus.count)}"></path>`);
      parts.push(`<text class="label" x="${x}" y="${y}">${escapeXml(slot.label)}</text>`);
    }

    const circle = (annuli[0]?.from ?? centre * 0.2) - 2;
    parts.push(`<circle class="centre" cx="${centre}" cy="${centre}" r="${circle}"></circle>`);
    parts.push(`<text class="scene" x="${centre}" y="${centre}">${escapeXml(snapshot.sceneName)}</text>`);
    return `<svg width="${snapshot.diameter}" height="${snapshot.diameter}" viewBox="0 0 ${snapshot.diameter} ${snapshot.diameter}">${parts.join('')}</svg>`;
  }

  protected override render() {
    if (this.error.length > 0) {
      return html`<p class="note">${this.error}</p>`;
    }

    if (!this.snapshot) {
      return html`<p class="note">…</p>`;
    }

    // Lit's template parser puts nested SVG shapes in the HTML namespace, where they never render;
    // the wheel is one generated <svg> string instead.
    return html`${unsafeSVG(this.svgMarkup(this.snapshot))}`;
  }
}

function escapeXml(value: string): string {
  return value
    .replaceAll('&', '&amp;')
    .replaceAll('<', '&lt;')
    .replaceAll('>', '&gt;')
    .replaceAll('"', '&quot;');
}
