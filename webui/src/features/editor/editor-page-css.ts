import { css } from 'lit';

export const editorPageCss = css`
    :host {
      display: flex;
      flex-direction: column;
      height: 100%;
      min-height: 0;
      min-width: 0;
      overflow: auto;
      background: var(--colorNeutralBackground1);
    }

    .workspace {
      display: flex;
      flex: 1;
      min-height: 0;
      min-width: 996px;
      padding: 14px 14px 0;
      box-sizing: border-box;
    }

    .palette-column {
      flex: none;
      width: 280px;
      min-height: 0;
      overflow: hidden;
      border-right: var(--strokeWidthThin) solid var(--colorNeutralStroke2);
    }

    .palette-column wg-editor-palette {
      height: 100%;
      min-height: 0;
    }

    .palette-splitter {
      flex: none;
      width: 6px;
      background: var(--colorNeutralBackground1);
      border-right: var(--strokeWidthThin) solid var(--colorNeutralStroke2);
    }

    .center {
      display: flex;
      flex: 1;
      flex-direction: column;
      min-width: 344px;
      min-height: 0;
    }

    .toolbar {
      display: flex;
      align-items: center;
      flex-wrap: wrap;
      gap: 2px;
      flex: none;
      min-height: 40px;
      padding: var(--spacingVerticalXS) var(--spacingHorizontalM);
      border-bottom: var(--strokeWidthThin) solid var(--colorNeutralStroke2);
      background: var(--colorNeutralBackground1);
    }

    .toolbar .sep {
      width: 1px;
      height: 18px;
      margin: 0 var(--spacingHorizontalXS);
      background: var(--colorNeutralStroke2);
    }

    .toolbar .grow {
      flex: 1;
    }

    .toolbar fluent-button {
      min-width: 0;
    }

    .saved {
      color: var(--colorStatusSuccessForeground1);
      font-size: var(--fontSizeBase200);
      margin: 0 var(--spacingHorizontalXS);
      white-space: nowrap;
    }

    .notice {
      flex: none;
      padding: var(--spacingVerticalS) var(--spacingHorizontalM) 0;
      color: var(--colorStatusSuccessForeground1);
      font-size: var(--fontSizeBase200);
    }

    .error {
      flex: none;
      padding: var(--spacingVerticalS) var(--spacingHorizontalM) 0;
    }

    .error fluent-message-bar {
      display: block;
    }

    .tree {
      flex: 1;
      min-height: 0;
      min-width: 0;
    }

    .splitter {
      flex: none;
      width: 4px;
      cursor: col-resize;
      background: transparent;
      border-left: var(--strokeWidthThin) solid var(--colorNeutralStroke2);
    }

    .splitter:hover,
    .splitter:focus-visible {
      background: var(--colorBrandBackground2);
      outline: none;
    }

    .inspector {
      flex: none;
      width: var(--inspector-width, 340px);
      min-width: 0;
      min-height: 0;
    }

    .empty {
      display: grid;
      place-content: center;
      gap: var(--spacingVerticalS);
      height: 100%;
      padding: var(--spacingVerticalXXL);
      text-align: center;
      color: var(--colorNeutralForeground3);
    }

    .empty .title {
      color: var(--colorNeutralForeground1);
      font-size: var(--fontSizeBase400);
    }
  `;
