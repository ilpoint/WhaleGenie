import { css } from 'lit';

/**
 * 原生 MacroMenuWindow 的尺寸：350 宽、667 高；内容盒 332 宽。
 * 标题 40 + 搜索 34+6 + 网格 248 + 分隔 44 + 网格 248 + 底栏 28+6 = 654，加边框与上下内边距 13 = 667。
 */
export const menuOverlayStyles = css`
  :host {
    display: block;
    width: 350px;
    height: 667px;
    background: transparent;
    font-family: var(--fontFamilyBase);
    color: var(--colorNeutralForeground1);
    user-select: none;
  }

  .panel {
    box-sizing: border-box;
    width: 350px;
    height: 667px;
    display: flex;
    flex-direction: column;
    padding: 3px 8px 8px;
    border-radius: 7px;
    border: var(--strokeWidthThin) solid var(--colorNeutralStroke2);
    background: var(--colorNeutralBackground1);
    overflow: hidden;
  }

  .titlebar {
    box-sizing: border-box;
    height: 40px;
    flex: none;
    display: flex;
    align-items: center;
    gap: 2px;
    margin: 0 -8px;
    padding: 0 8px;
    border-radius: 6px 6px 0 0;
    background: var(--colorNeutralBackground2);
    cursor: move;
  }

  .logo {
    width: 22px;
    height: 22px;
    object-fit: contain;
  }

  .app-title {
    flex: 1;
    min-width: 0;
    margin-left: 4px;
    font-size: 13px;
    font-weight: var(--fontWeightSemibold);
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
  }

  .tool {
    width: 26px;
    height: 26px;
    padding: 0;
    display: inline-flex;
    align-items: center;
    justify-content: center;
    border: none;
    border-radius: 5px;
    background: transparent;
    color: var(--colorNeutralForeground2);
    cursor: pointer;
  }

  .tool:hover,
  .tool.on {
    background: var(--colorNeutralBackground3Hover);
    color: var(--colorNeutralForeground1);
  }

  .tool.pin.on {
    background: var(--colorBrandBackground2);
    color: var(--colorBrandForeground1);
  }

  .tool.close:hover {
    background: var(--colorPaletteRedBackground2);
    color: var(--colorPaletteRedForeground1);
  }

  .search-slot {
    box-sizing: border-box;
    height: 34px;
    flex: none;
    margin-bottom: 6px;
    opacity: 0;
    pointer-events: none;
  }

  .search-slot.open {
    opacity: 1;
    pointer-events: auto;
  }

  .search-slot input {
    box-sizing: border-box;
    width: 100%;
    height: 34px;
    padding: 0 8px;
    border-radius: 6px;
    border: var(--strokeWidthThin) solid var(--colorNeutralStroke1);
    background: var(--colorNeutralBackground3);
    color: inherit;
    font: inherit;
    outline: none;
  }

  .search-slot input:focus {
    border-color: var(--colorBrandStroke1);
  }

  .grid {
    display: grid;
    grid-template-columns: repeat(4, 80px);
    grid-auto-rows: 80px;
    gap: 4px;
    flex: none;
  }

  button.cell {
    box-sizing: border-box;
    width: 80px;
    height: 80px;
    display: flex;
    align-items: center;
    justify-content: center;
    padding: 2px;
    border-radius: 4px;
    border: var(--strokeWidthThin) solid var(--colorNeutralStroke2);
    background: var(--colorNeutralBackground3);
    color: var(--colorNeutralForeground1);
    font: inherit;
    cursor: pointer;
    overflow: hidden;
    transition: opacity 80ms linear;
  }

  button.cell:hover {
    background: var(--colorNeutralBackground3Hover);
  }

  button.cell.empty {
    background: transparent;
    border-style: dashed;
    border-color: var(--colorNeutralStroke3);
    color: var(--colorNeutralForeground3);
    cursor: default;
  }

  button.cell.dimmed {
    opacity: 0.25;
  }

  button.cell img {
    max-width: 74px;
    max-height: 74px;
    object-fit: contain;
  }

  .name {
    max-width: 72px;
    max-height: 42px;
    font-size: 10.5px;
    line-height: 1.2;
    text-align: center;
    overflow: hidden;
    text-overflow: ellipsis;
    overflow-wrap: anywhere;
  }

  .divider {
    box-sizing: border-box;
    height: 44px;
    flex: none;
    display: flex;
    align-items: center;
    gap: 6px;
    min-width: 0;
  }

  .rule {
    flex: 1;
    min-width: 0;
    padding-left: 2px;
    font-size: 10px;
    color: var(--colorNeutralForeground3);
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
  }

  .dot-rows {
    display: flex;
    flex-direction: column;
    gap: 2px;
    align-items: flex-end;
  }

  .dots {
    display: flex;
    align-items: center;
    gap: 2px;
    min-height: 16px;
  }

  .dot {
    width: 12px;
    height: 16px;
    padding: 0;
    display: inline-flex;
    align-items: center;
    justify-content: center;
    border: none;
    background: transparent;
    cursor: pointer;
  }

  .dot span {
    width: 4px;
    height: 4px;
    border-radius: 50%;
    background: var(--colorNeutralForeground3);
  }

  .dot.on span {
    width: 6px;
    height: 6px;
    background: var(--colorBrandBackground);
  }

  .dot.add {
    width: 16px;
    color: var(--colorNeutralForeground2);
  }

  .footer {
    box-sizing: border-box;
    height: 28px;
    flex: none;
    margin-top: 6px;
    display: flex;
    align-items: center;
    justify-content: space-between;
  }

  .footer button {
    width: 28px;
    height: 28px;
    padding: 0;
    display: inline-flex;
    align-items: center;
    justify-content: center;
    border: none;
    border-radius: 5px;
    background: transparent;
    color: var(--colorNeutralForeground2);
    cursor: pointer;
  }

  .footer button:hover:not(:disabled) {
    background: var(--colorNeutralBackground3Hover);
  }

  .footer button:disabled {
    opacity: 0.4;
    cursor: default;
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
    min-width: 148px;
    padding: 4px;
    border-radius: 8px;
    border: var(--strokeWidthThin) solid var(--colorNeutralStroke2);
    background: var(--colorNeutralBackground1);
    box-shadow: var(--shadow16);
  }

  .context button {
    display: flex;
    align-items: center;
    gap: 8px;
    padding: 6px 10px;
    border: none;
    border-radius: 5px;
    background: transparent;
    color: inherit;
    font: inherit;
    font-size: 12px;
    text-align: left;
    cursor: pointer;
  }

  .context button:hover {
    background: var(--colorNeutralBackground3Hover);
  }
`;
