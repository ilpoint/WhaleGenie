import { LitElement, css, html } from 'lit';
import { customElement, property, state } from 'lit/decorators.js';
import { unsafeSVG } from 'lit/directives/unsafe-svg.js';
import { iconPath, svgPath } from './icons.js';

/**
 * One Lucide icon, drawn from the path the program sent. The path lives in a 24x24 box; colour
 * stays currentColor so an icon reads like the button around it.
 */
@customElement('wg-icon')
export class WgIcon extends LitElement {
  @property({ type: String })
  name = '';

  @property({ type: Number })
  size = 16;

  @property({ type: Number })
  strokeWidth = 1.7;

  @state()
  private path: string | null = null;

  static styles = css`
    :host {
      display: inline-flex;
      align-items: center;
      justify-content: center;
      line-height: 0;
      flex: none;
    }

    svg {
      display: block;
    }
  `;

  protected override async updated(changed: Map<string, unknown>): Promise<void> {
    if (!changed.has('name')) {
      return;
    }

    const path = this.name.length > 0 ? await iconPath(this.name) : null;
    this.path = path === null ? null : svgPath(path);
  }

  protected override render() {
    if (!this.path) {
      return html``;
    }

    return html`
      ${unsafeSVG(`<svg viewBox="0 0 24 24" width="${this.size}" height="${this.size}"
        fill="none" stroke="currentColor" stroke-width="${this.strokeWidth}"
        stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="${this.path}"></path></svg>`)}
    `;
  }
}
