import { LitElement, css, html, nothing } from 'lit';
import { customElement, state } from 'lit/decorators.js';
import { unsafeSVG } from 'lit/directives/unsafe-svg.js';
import { bridgeApi } from '../../shared/bridge/bridge.js';
import type {
  SurfaceWheelAim,
  SurfaceWheelRing,
  SurfaceWheelSlot,
  SurfaceWheelSnapshot,
} from '../../shared/bridge/protocol.js';
import { iconPath, svgPath } from '../../shared/icons/icons.js';
import { strings } from '../../shared/strings/strings.js';
import { StoreController } from '../../shared/lit/store-controller.js';
import { fileUrl } from '../../shared/files/file-url.js';

/**
 * 轮盘浮层：C# 给出圈、每一圈的位置数和已填的格子，页面只负责画。
 *
 * 画布是直径的两倍，和原生一样：引导线要跟到圈外的指针。窗口本身透明且点击穿透，指针由全局钩子读。
 */
@customElement('wg-overlay-wheel')
export class WgOverlayWheel extends LitElement {
  @state()
  private snapshot: SurfaceWheelSnapshot | null = null;

  @state()
  private aim: SurfaceWheelAim | null = null;

  @state()
  private pointer: { x: number; y: number } | null = null;

  private baseMarkup = '';

  @state()
  private error = '';

  @state()
  private animating = false;

  private animationTimer = 0;

  private changedFrame = false;

  @state()
  private animationMs = 160;

  private readonly iconCache = new Map<string, string | null>();

  protected readonly stringsController = new StoreController(this, strings);

  static styles = css`
    :host {
      position: fixed;
      inset: 0;
      display: block;
      width: 100vw;
      height: 100vh;
      overflow: visible;
      background: transparent;
      font-family: var(--fontFamilyBase);
      color: var(--colorNeutralForeground1);
      user-select: none;
    }

    svg {
      display: block;
      overflow: visible;
    }

    .base-svg {
      display: block;
    }

    .aim-layer {
      position: absolute;
      left: 0;
      top: 0;
      overflow: visible;
      pointer-events: none;
    }

    .surface {
      fill: var(--colorNeutralBackground1);
      stroke: var(--colorNeutralStroke2);
      stroke-width: 1;
    }

    .gloss {
      opacity: 0.9;
    }

    .sector {
      fill: var(--colorNeutralBackground1);
      stroke: var(--colorNeutralStroke2);
      stroke-width: 0.8;
    }

    .sector.global {
      fill: var(--colorNeutralBackground3);
    }

    .sector.overridden {
      fill: var(--colorPaletteYellowBackground2);
    }

    .sector.extended {
      fill: var(--colorNeutralBackground2);
    }

    .sector.aimed {
      fill: var(--colorBrandBackground2);
    }

    .sector.aimed.border {
      stroke: var(--colorBrandStroke1);
      stroke-width: 2.5;
    }

    .label {
      font-size: 1em;
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
      font-size: 1em;
      fill: var(--colorNeutralForeground2);
      text-anchor: middle;
      dominant-baseline: middle;
    }

    .guide {
      stroke: var(--colorBrandStroke1);
      stroke-width: 2;
      fill: none;
    }

    .guide.extended {
      stroke-width: 2.5;
      stroke-dasharray: 6 5;
    }

    .guide-dot {
      fill: var(--colorBrandBackground);
    }

    .label.small {
      font-size: 0.85em;
    }

    .wheel.fixed .surface,
    .wheel.fixed .sector {
      fill: #f3f3f3;
    }

    .note {
      padding: 10px;
      font-size: 12px;
      color: var(--colorNeutralForeground3);
    }

    .wheel {
      position: absolute;
      display: block;
      transform-origin: center;
    }

    .wheel text {
      font-family: var(--fontFamilyBase);
    }

    .wheel.pop {
      animation: wheel-pop var(--wheel-pop-ms, 160ms) cubic-bezier(0.2, 0.9, 0.3, 1);
    }

    @keyframes wheel-pop {
      from { opacity: 0.25; transform: scale(0.92); }
      to { opacity: 1; transform: scale(1); }
    }
  `;

  protected override async firstUpdated(): Promise<void> {
    window.addEventListener('focus', () => void this.reload());
    bridgeApi.on('surface.wheel.changed', (next) => {
      this.changedFrame = true;
      this.snapshot = next;
      this.aim = next.aim;
      this.pointer = null;
      this.refreshBase(next);
      this.preload(next);
      if (next.opening) {
        void this.updateComplete.then(() => {
          void bridgeApi.call('surface.wheel.ready').catch(() => undefined);
        });
      }
    });
    bridgeApi.on('surface.wheel.aim', (event) => {
      this.pointer = { x: event.x, y: event.y };
      this.aim = event.hasAim && event.ring
        ? { ring: event.ring, index: event.index, x: event.x, y: event.y }
        : null;
      this.requestUpdate();
    });
    bridgeApi.on('surface.wheel.closed', () => {
      this.snapshot = null;
      this.aim = null;
      this.pointer = null;
      this.baseMarkup = '';
      this.changedFrame = false;
      this.error = '';
      window.clearTimeout(this.animationTimer);
      this.animating = false;
      this.requestUpdate();
    });
    bridgeApi.on('surface.wheel.opened', (event) => {
      window.clearTimeout(this.animationTimer);
      this.animationMs = event.ms > 0 ? event.ms : 160;
      this.animating = false;
      if (!event.pop) {
        return;
      }

      requestAnimationFrame(() => {
        this.animating = true;
        this.animationTimer = window.setTimeout(() => { this.animating = false; }, this.animationMs);
      });
    });
    await this.reload();
  }

  private async reload(): Promise<void> {
    try {
      const state = await bridgeApi.call<SurfaceWheelSnapshot>('surface.wheel.state');
      // A changed frame may have arrived while the initial state request was in flight; it is
      // newer and carries the full-screen centre, so do not overwrite it with the warm state.
      if (!this.changedFrame || !this.snapshot) {
        this.snapshot = state;
        this.aim = state.aim;
        this.refreshBase(state);
        this.preload(state);
      }
      this.error = '';
    } catch (failure) {
      this.error = failure instanceof Error ? failure.message : String(failure);
    }
  }

  /** 把这一帧用到的 Lucide 路径先取回来，取到一个就让画面重画一次。 */
  private preload(snapshot: SurfaceWheelSnapshot): void {
    for (const slot of snapshot.slots) {
      if (slot.iconKind !== 'builtin' || !slot.iconValue || this.iconCache.has(slot.iconValue)) {
        continue;
      }

      const name = slot.iconValue;
      this.iconCache.set(name, null);
      void iconPath(name).then((path) => {
        this.iconCache.set(name, path);
        if (this.snapshot) {
          this.refreshBase(this.snapshot);
        }
        this.requestUpdate();
      });
    }
  }

  protected override render() {
    if (this.error.length > 0) {
      return html`<p class="note">${this.error}</p>`;
    }

    if (!this.snapshot) {
      return nothing;
    }

    const centerX = this.snapshot.centerX >= 0
      ? this.snapshot.centerX : this.snapshot.canvasSize / 2;
    const centerY = this.snapshot.centerY >= 0
      ? this.snapshot.centerY : this.snapshot.canvasSize / 2;
    const left = centerX - this.snapshot.canvasSize / 2;
    const top = centerY - this.snapshot.canvasSize / 2;
    return html`<div class="wheel ${this.animating ? 'pop' : ''} ${this.snapshot.followTheme ? '' : 'fixed'}"
      style=${'left:' + left + 'px;top:' + top + 'px;font-size:' + this.snapshot.fontSize + 'px;--wheel-pop-ms:' + this.animationMs + 'ms'}>
      <div class="base">${unsafeSVG(this.baseMarkup)}</div>
      <svg class="aim-layer" width=${this.snapshot.canvasSize} height=${this.snapshot.canvasSize}
        viewBox=${'0 0 ' + this.snapshot.canvasSize + ' ' + this.snapshot.canvasSize}>
        ${unsafeSVG(aimMarkup(this.snapshot, this.aim, this.pointer))}
      </svg></div>`;
  }

  private refreshBase(snapshot: SurfaceWheelSnapshot): void {
    const size = snapshot.canvasSize;
    const centre = size / 2;
    const edge = snapshot.rings.length > 0
      ? snapshot.rings[snapshot.rings.length - 1]!.to
      : snapshot.diameter / 2;
    const bySlot = new Map<string, SurfaceWheelSlot>();
    for (const slot of snapshot.slots) {
      bySlot.set(slot.ring + ':' + slot.index, slot);
    }

    this.baseMarkup = '<svg class="base-svg" width="' + size + '" height="' + size
      + '" viewBox="0 0 ' + size + ' ' + size + '">'
      + [...surfaceMarkup(snapshot, centre, edge),
        ...sectorMarkup(snapshot, centre, bySlot),
        ...slotMarkup(snapshot, centre, this.iconCache),
        centreMarkup(snapshot, centre)].join('') + '</svg>';
  }
}

function surfaceMarkup(snapshot: SurfaceWheelSnapshot, centre: number,
  edge: number): string[] {
  const parts = [
    '<circle class="surface" cx="' + centre + '" cy="' + centre + '" r="' + edge + '"></circle>',
  ];
  if (snapshot.gloss) {
    parts.push(
      '<defs><linearGradient id="wg-wheel-gloss" x1="0" y1="0" x2="0" y2="1">'
      + '<stop offset="0" stop-color="#fff" stop-opacity="0.22"/>'
      + '<stop offset="0.55" stop-color="#fff" stop-opacity="0"/>'
      + '<stop offset="1" stop-color="#000" stop-opacity="0.10"/></linearGradient></defs>',
      '<circle class="gloss" cx="' + centre + '" cy="' + centre + '" r="' + edge
      + '" fill="url(#wg-wheel-gloss)"></circle>',
    );
  }
  return parts;
}

function sectorMarkup(snapshot: SurfaceWheelSnapshot, centre: number,
  bySlot: Map<string, SurfaceWheelSlot>): string[] {
  const parts: string[] = [];
  for (const ring of snapshot.rings) {
    for (let index = 0; index < ring.count; index += 1) {
      const slot = bySlot.get(ring.ring + ':' + index);
      parts.push('<path class="sector ' + (slot?.tone ?? 'empty')
        + '" d="' + sectorPath(centre, ring.from, ring.to, index, ring.count) + '"></path>');
    }
  }

  return parts;
}

function slotMarkup(snapshot: SurfaceWheelSnapshot, centre: number,
  icons: Map<string, string | null>): string[] {
  const parts: string[] = [];
  for (const slot of snapshot.slots) {
    const ring = snapshot.rings.find((candidate) => candidate.ring === slot.ring);
    if (!ring) {
      continue;
    }

    const [x, y] = labelPoint(centre, ring, slot.index);
    const path = slot.iconKind === 'builtin' && slot.iconValue
      ? icons.get(slot.iconValue)
      : null;
    let hasIcon = false;
    if (path) {
      hasIcon = true;
      parts.push('<path d="' + escapeXml(svgPath(path)) + '" fill="none" stroke="currentColor"'
        + ' stroke-width="1.6" stroke-linecap="round" stroke-linejoin="round"'
        + ' transform="translate(' + (x - 12).toFixed(2) + ' ' + (y - 18).toFixed(2)
        + ') scale(1.0)"></path>');
    } else if (slot.iconKind === 'image' && slot.iconValue) {
      hasIcon = true;
      parts.push('<image x="' + (x - 14).toFixed(2) + '" y="' + (y - 20).toFixed(2)
        + '" width="28" height="28" href="' + escapeXml(fileUrl(slot.iconValue)) + '"></image>');
    }

    if (!hasIcon || !snapshot.hideLabelWithIcon) {
      const labelY = hasIcon ? y + 17 : y;
      parts.push('<text class="label' + (hasIcon ? ' small' : '') + '" x="' + x.toFixed(2)
        + '" y="' + labelY.toFixed(2) + '">' + escapeXml(slot.label) + '</text>');
    }
  }

  return parts;
}

function centreMarkup(snapshot: SurfaceWheelSnapshot, centre: number): string {
  const radius = (snapshot.rings[0]?.from ?? snapshot.diameter * 0.11) - 2;
  const label = snapshot.centerName
    ? ''
    : '<text class="scene" x="' + centre + '" y="' + centre + '">'
      + escapeXml(snapshot.centerText) + '</text>';
  return '<circle class="centre" cx="' + centre + '" cy="' + centre + '" r="' + radius
    + '"></circle>' + label;
}

export function aimMarkup(snapshot: SurfaceWheelSnapshot, aim: SurfaceWheelAim | null,
  pointer: { x: number; y: number } | null): string {
  const centre = snapshot.canvasSize / 2;
  const parts: string[] = [];
  if (snapshot.centerName) {
    const slot = aim
      ? snapshot.slots.find((candidate) => candidate.ring === aim.ring
        && candidate.index === aim.index)
      : undefined;
    const label = slot && slot.label.length > 0 ? slot.label : snapshot.centerText;
    parts.push('<text class="scene" x="' + centre + '" y="' + centre + '">'
      + escapeXml(label) + '</text>');
  }

  if (aim) {
    const ring = snapshot.rings.find((candidate) => candidate.ring === aim.ring);
    if (ring) {
      parts.push('<path class="sector aimed'
        + (snapshot.hoverBorder ? ' border' : '')
        + '" d="' + sectorPath(centre, ring.from, ring.to, aim.index, ring.count)
        + '"></path>');
    }
  }

  if (pointer && snapshot.showGuide) {
    const endX = centre + pointer.x;
    const endY = centre + pointer.y;
    const extended = aim?.ring === 'extended';
    parts.push('<line class="guide' + (extended ? ' extended' : '') + '" x1="' + centre
      + '" y1="' + centre + '" x2="' + endX.toFixed(2) + '" y2="' + endY.toFixed(2)
      + '"></line>',
    '<circle class="guide-dot" cx="' + endX.toFixed(2) + '" cy="' + endY.toFixed(2)
      + '" r="' + (extended ? 5 : 3.5) + '"></circle>');
  }

  return parts.join('');
}

/** 一格扇区：一段外弧加一段内弧，所有平台都按同一组点画。 */
function sectorPath(centre: number, from: number, to: number, index: number, count: number): string {
  const step = (Math.PI * 2) / count;
  const start = index * step - step / 2;
  const points: string[] = [];
  const segments = 12;
  for (let part = 0; part <= segments; part += 1) {
    const at = start + step * (part / segments);
    points.push(point(centre, to, at));
  }

  for (let part = segments; part >= 0; part -= 1) {
    const at = start + step * (part / segments);
    points.push(point(centre, from, at));
  }

  return 'M ' + points.join(' L ') + ' Z';
}

function labelPoint(centre: number, ring: SurfaceWheelRing, index: number): [number, number] {
  const radius = (ring.from + ring.to) / 2;
  const angle = index * Math.PI * 2 / ring.count;
  return [centre + radius * Math.sin(angle), centre - radius * Math.cos(angle)];
}

function point(centre: number, radius: number, angle: number): string {
  return (centre + radius * Math.sin(angle)).toFixed(2) + ' '
    + (centre - radius * Math.cos(angle)).toFixed(2);
}
function escapeXml(value: string): string {
  return value
    .replaceAll('&', '&amp;')
    .replaceAll('<', '&lt;')
    .replaceAll('>', '&gt;')
    .replaceAll('"', '&quot;');
}