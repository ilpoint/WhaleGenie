import { css } from 'lit';

export const windowRuleDialogCss = css`
    :host {
      display: block;
      position: fixed;
      inset: 0;
      z-index: 80;
    }

    .backdrop {
      position: fixed;
      inset: 0;
      z-index: 80;
      display: grid;
      place-items: center;
      padding: var(--spacingVerticalXL);
      background: var(--shell-scrim);
    }

    .dialog {
      width: min(760px, 100%);
      max-height: min(640px, 100%);
      display: grid;
      grid-template-rows: auto 1fr auto;
      gap: var(--spacingVerticalM);
      padding: var(--spacingVerticalL);
      border: var(--strokeWidthThin) solid var(--colorNeutralStroke1);
      border-radius: var(--borderRadiusLarge);
      background: var(--colorNeutralBackground1);
      box-shadow: var(--shadow16);
    }

    h2 {
      margin: 0;
      font-size: var(--fontSizeBase400);
    }

    .hint {
      margin: 0;
      color: var(--colorNeutralForeground3);
      font-size: var(--fontSizeBase200);
    }

    .body {
      display: grid;
      grid-template-columns: minmax(220px, 0.9fr) minmax(280px, 1.1fr);
      gap: var(--spacingHorizontalL);
      min-height: 0;
    }

    .panel {
      display: grid;
      gap: var(--spacingVerticalS);
      min-height: 0;
    }

    .windows {
      overflow: auto;
      min-height: 0;
      display: grid;
      align-content: start;
      gap: var(--spacingVerticalXS);
    }

    .window {
      display: grid;
      gap: 2px;
      width: 100%;
      text-align: left;
      padding: var(--spacingVerticalS) var(--spacingHorizontalS);
      border: var(--strokeWidthThin) solid transparent;
      border-radius: var(--borderRadiusMedium);
      background: transparent;
      color: inherit;
      font: inherit;
      cursor: default;
    }

    .window:hover {
      background: var(--colorNeutralBackground1Hover);
    }

    .window[data-selected='true'] {
      border-color: var(--colorBrandStroke1);
      background: var(--colorBrandBackground2);
    }

    .window strong {
      font-size: var(--fontSizeBase300);
      font-weight: var(--fontWeightSemibold);
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
    }

    .window span {
      color: var(--colorNeutralForeground3);
      font-size: var(--fontSizeBase200);
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
    }

    .row {
      display: grid;
      grid-template-columns: 130px minmax(0, 1fr);
      align-items: center;
      gap: var(--spacingHorizontalM);
    }

    .row label {
      color: var(--colorNeutralForeground2);
    }

    .checks {
      display: flex;
      flex-wrap: wrap;
      gap: var(--spacingHorizontalM);
    }

    .actions {
      display: flex;
      flex-wrap: wrap;
      justify-content: flex-end;
      gap: var(--spacingHorizontalS);
    }

    .actions .left {
      margin-right: auto;
    }

    .error {
      color: var(--colorPaletteRedForeground1);
      margin: 0;
    }

    .preview {
      display: grid;
      gap: 2px;
      padding: var(--spacingVerticalS);
      border: var(--strokeWidthThin) solid var(--colorNeutralStroke2);
      border-radius: var(--borderRadiusMedium);
      background: var(--colorNeutralBackground2);
      font-size: var(--fontSizeBase100);
      color: var(--colorNeutralForeground2);
    }

    .preview strong {
      color: var(--colorNeutralForeground1);
    }

    input,
    select {
      font: inherit;
      width: 100%;
    }
  `;
