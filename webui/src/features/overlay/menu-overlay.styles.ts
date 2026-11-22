import { css } from 'lit';

/** The menu overlay's panel, grid, pager and context menu, all drawn over a transparent host. */
export const menuOverlayStyles = css`

    :host {
      display: block;
      width: max-content;
      background: transparent;
      font-family: var(--fontFamilyBase);
      color: var(--colorNeutralForeground1);
      user-select: none;
    }

    .panel {
      margin: 10px;
      padding: 10px;
      border-radius: 12px;
      background: var(--colorNeutralBackground1);
      box-shadow: var(--shadow16);
      border: var(--strokeWidthThin) solid var(--colorNeutralStroke2);
    }

    .header {
      display: flex;
      align-items: center;
      justify-content: space-between;
      margin-bottom: 6px;
    }

    .header .title {
      font-size: 12px;
      font-weight: var(--fontWeightSemibold);
      color: var(--colorNeutralForeground2);
    }

    .header .pin {
      width: 24px;
      height: 22px;
      padding: 0;
      border: none;
      border-radius: 5px;
      background: transparent;
      font-size: 12px;
      cursor: pointer;
      opacity: 0.65;
    }

    .header .pin.on {
      background: var(--colorBrandBackground2);
      opacity: 1;
    }

    .search {
      margin-bottom: 8px;
    }

    .search input {
      width: 100%;
      box-sizing: border-box;
      height: 28px;
      padding: 0 8px;
      border-radius: 6px;
      border: var(--strokeWidthThin) solid var(--colorNeutralStroke1);
      background: var(--colorNeutralBackground3);
      color: inherit;
      font: inherit;
      outline: none;
    }

    .search input:focus {
      border-color: var(--colorBrandStroke1);
    }

    .grid {
      display: grid;
      grid-template-columns: repeat(4, 78px);
      grid-auto-rows: 70px;
      gap: 4px;
    }

    button.cell {
      display: flex;
      flex-direction: column;
      align-items: center;
      justify-content: center;
      gap: 4px;
      padding: 4px;
      border-radius: 8px;
      border: var(--strokeWidthThin) solid transparent;
      background: var(--colorNeutralBackground3);
      color: inherit;
      font: inherit;
      cursor: pointer;
      overflow: hidden;
    }

    button.cell:hover {
      background: var(--colorNeutralBackground3Hover);
      border-color: var(--colorNeutralStroke2);
    }

    button.cell:active {
      background: var(--colorNeutralBackground3Pressed);
    }

    button.cell.empty {
      background: transparent;
      border: var(--strokeWidthThin) dashed var(--colorNeutralStroke3);
      color: var(--colorNeutralForeground4);
      cursor: default;
    }

    button.cell img {
      width: 26px;
      height: 26px;
      object-fit: contain;
    }

    .glyph {
      width: 26px;
      height: 26px;
      border-radius: 6px;
      display: grid;
      place-items: center;
      background: var(--colorNeutralBackground1);
      font-size: 12px;
      font-weight: var(--fontWeightSemibold);
    }

    .name {
      max-width: 70px;
      font-size: 11px;
      line-height: 1.2;
      text-align: center;
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
    }

    .pages {
      display: flex;
      gap: 6px;
      justify-content: center;
      margin: 8px 0;
    }

    .pages button {
      width: 8px;
      height: 8px;
      padding: 0;
      border: none;
      border-radius: 50%;
      background: var(--colorNeutralStroke3);
      cursor: pointer;
    }

    .pages button[aria-current='true'] {
      background: var(--colorBrandBackground);
    }

    .pages button.add,
    .pages button.remove {
      width: 16px;
      height: 16px;
      border-radius: 4px;
      background: var(--colorNeutralBackground3);
      color: var(--colorNeutralForeground2);
      font-size: 11px;
      line-height: 1;
    }

    .pages button.add:hover,
    .pages button.remove:hover {
      background: var(--colorNeutralBackground3Hover);
    }

    .section-title {
      margin: 6px 0 4px;
      font-size: 11px;
      color: var(--colorNeutralForeground3);
    }

    .note {
      padding: 10px;
      font-size: 12px;
      color: var(--colorNeutralForeground3);
    }

    .context {
      position: fixed;
      z-index: 10;
      display: grid;
      min-width: 120px;
      padding: 4px;
      border-radius: 8px;
      border: var(--strokeWidthThin) solid var(--colorNeutralStroke2);
      background: var(--colorNeutralBackground1);
      box-shadow: var(--shadow16);
    }

    .context button {
      padding: 6px 10px;
      border: none;
      border-radius: 5px;
      background: transparent;
      color: inherit;
      font: inherit;
      text-align: left;
      cursor: pointer;
    }

    .context button:hover {
      background: var(--colorNeutralBackground3Hover);
    }

`;