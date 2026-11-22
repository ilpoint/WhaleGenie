import { css } from 'lit';

export const runPageCss = css`
    :host {
      display: grid;
      grid-template-columns: 280px minmax(0, 1fr);
      gap: var(--spacingHorizontalL);
      padding: var(--spacingVerticalL) var(--spacingHorizontalXXL);
      height: 100%;
      box-sizing: border-box;
    }

    h1 {
      margin: 0 0 var(--spacingVerticalS);
      font-size: var(--fontSizeBase500);
      font-weight: var(--fontWeightSemibold);
    }

    .side {
      min-width: 0;
    }

    .macros, .steps, .log {
      display: grid;
      gap: var(--spacingVerticalXS);
      align-content: start;
    }

    .macros button, .steps button {
      text-align: left;
    }

    .panel {
      display: grid;
      grid-template-rows: auto auto auto minmax(120px, 1fr) minmax(100px, 0.7fr) auto;
      gap: var(--spacingVerticalM);
      min-width: 0;
    }

    .toolbar {
      display: flex;
      flex-wrap: wrap;
      gap: var(--spacingHorizontalS);
      align-items: center;
    }

    .status {
      color: var(--colorNeutralForeground2);
    }

    .args {
      display: grid;
      gap: var(--spacingVerticalS);
      padding: var(--spacingVerticalS) var(--spacingHorizontalM);
      border: var(--strokeWidthThin) solid var(--colorNeutralStroke2);
      border-radius: var(--borderRadiusMedium);
      background: var(--colorNeutralBackground1);
    }

    .arg {
      display: grid;
      gap: var(--spacingVerticalXS);
    }

    .arg .hint {
      color: var(--colorNeutralForeground3);
      font-size: var(--fontSizeBase200);
    }

    .ask {
      display: grid;
      gap: var(--spacingVerticalS);
      padding: var(--spacingVerticalS) var(--spacingHorizontalM);
      border: var(--strokeWidthThin) solid var(--colorBrandStroke1);
      border-radius: var(--borderRadiusMedium);
      background: var(--colorNeutralBackground2);
    }

    .step {
      display: grid;
      grid-template-columns: 22px minmax(0, 1fr);
      gap: var(--spacingHorizontalS);
      align-items: start;
      padding: var(--spacingVerticalXS) var(--spacingHorizontalS);
      border-left: var(--strokeWidthThick) solid transparent;
    }

    .step.current {
      border-left-color: var(--colorBrandStroke1);
      background: var(--colorNeutralBackground2);
    }

    .step .body {
      min-width: 0;
    }

    .rule {
      display: grid;
      grid-template-columns: 72px minmax(0, 1fr);
      gap: var(--spacingHorizontalS);
      margin-top: var(--spacingVerticalXS);
    }

    .step .detail {
      color: var(--colorNeutralForeground3);
      font-size: var(--fontSizeBase200);
      overflow-wrap: anywhere;
    }

    .log-line {
      display: grid;
      grid-template-columns: 72px 64px minmax(0, 1fr);
      gap: var(--spacingHorizontalS);
      font-size: var(--fontSizeBase200);
    }

    .log-line .time {
      color: var(--colorNeutralForeground3);
    }

    .look {
      margin: 0;
      display: grid;
      gap: var(--spacingVerticalXS);
    }

    .look img {
      max-width: 100%;
      max-height: 220px;
      object-fit: contain;
      justify-self: start;
      border: var(--strokeWidthThin) solid var(--colorNeutralStroke2);
    }

    .look figcaption {
      color: var(--colorNeutralForeground3);
      font-size: var(--fontSizeBase200);
    }

    .vars h3 {
      margin: 0 0 var(--spacingVerticalXS);
      font-size: var(--fontSizeBase300);
    }

    .var-grid {
      display: grid;
      grid-template-columns: minmax(120px, 1fr) 110px minmax(0, 1.4fr);
      gap: var(--spacingVerticalXS) var(--spacingHorizontalS);
      font-size: var(--fontSizeBase200);
    }

    .var {
      display: contents;
    }

    .var span:last-child {
      overflow-wrap: anywhere;
    }

    .error {
      color: var(--colorPaletteRedForeground1);
    }
  `;
