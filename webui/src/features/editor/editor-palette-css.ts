import { css } from 'lit';

export const editorPaletteCss = css`
    :host {
      display: flex;
      flex-direction: column;
      height: 100%;
      min-height: 0;
      background: var(--colorNeutralBackground3);
    }

    .sources {
      display: grid;
      grid-template-columns: repeat(4, minmax(0, 1fr));
      gap: 2px;
      padding: var(--spacingVerticalS) var(--spacingHorizontalS) var(--spacingVerticalXS);
      border-bottom: var(--strokeWidthThin) solid var(--colorNeutralStroke2);
    }

    .source {
      display: flex;
      flex-direction: column;
      align-items: center;
      justify-content: center;
      gap: 2px;
      min-width: 0;
      min-height: 44px;
      padding: var(--spacingVerticalXS) 2px;
      border: var(--strokeWidthThin) solid transparent;
      border-radius: var(--borderRadiusMedium);
      background: transparent;
      color: var(--colorNeutralForeground2);
      font-size: var(--fontSizeBase200);
      line-height: 1.2;
      text-align: center;
      cursor: pointer;
    }

    .source:hover {
      background: var(--colorSubtleBackgroundHover);
      color: var(--colorNeutralForeground1);
    }

    .source[aria-selected='true'] {
      background: var(--colorBrandBackground2);
      border-color: var(--colorBrandStroke1);
      color: var(--colorBrandForeground2);
    }

    .source:focus-visible {
      outline: var(--strokeWidthThicker) solid var(--colorStrokeFocus2);
      outline-offset: -1px;
    }

    .source svg {
      width: 14px;
      height: 14px;
    }

    .body {
      display: flex;
      flex: 1;
      min-height: 0;
    }

    .rail {
      width: 64px;
      flex: none;
      overflow-y: auto;
      border-right: var(--strokeWidthThin) solid var(--colorNeutralStroke2);
      padding: var(--spacingVerticalXS) 3px;
    }

    .rail button {
      display: flex;
      flex-direction: column;
      align-items: center;
      justify-content: center;
      gap: 2px;
      width: 100%;
      min-height: 46px;
      padding: 2px;
      border: var(--strokeWidthThin) solid transparent;
      border-radius: var(--borderRadiusMedium);
      background: transparent;
      color: var(--colorNeutralForeground3);
      font-size: var(--fontSizeBase100);
      line-height: 1.2;
      cursor: pointer;
    }

    .rail button:hover {
      background: var(--colorSubtleBackgroundHover);
      color: var(--colorNeutralForeground1);
    }

    .rail button[aria-pressed='true'] {
      background: var(--colorBrandBackground2);
      border-color: var(--colorBrandStroke1);
      color: var(--colorBrandForeground2);
    }

    .rail button:focus-visible {
      outline: var(--strokeWidthThicker) solid var(--colorStrokeFocus2);
      outline-offset: -1px;
    }

    .rail svg {
      width: 16px;
      height: 16px;
    }

    .items {
      display: flex;
      flex: 1;
      flex-direction: column;
      min-width: 0;
      min-height: 0;
    }

    .search {
      flex: none;
      margin: var(--spacingVerticalXS) var(--spacingHorizontalS) 0;
      padding: var(--spacingVerticalXS) var(--spacingHorizontalS);
      border: var(--strokeWidthThin) solid var(--colorNeutralStroke1);
      border-bottom: var(--strokeWidthThicker) solid var(--colorNeutralStrokeAccessible);
      border-radius: var(--borderRadiusMedium);
      background: var(--colorNeutralBackground1);
      color: var(--colorNeutralForeground1);
      font: inherit;
      font-size: var(--fontSizeBase200);
    }

    .search:focus-visible {
      outline: var(--strokeWidthThicker) solid var(--colorStrokeFocus2);
      outline-offset: -1px;
    }

    .search::placeholder {
      color: var(--colorNeutralForeground4);
    }

    .scroll {
      flex: 1;
      min-height: 0;
      overflow: auto;
      padding: var(--spacingVerticalXS) var(--spacingHorizontalS) var(--spacingVerticalM);
    }

    .section-title {
      margin: var(--spacingVerticalS) 0 var(--spacingVerticalXS);
      color: var(--colorNeutralForeground3);
      font-size: var(--fontSizeBase100);
      font-weight: var(--fontWeightSemibold);
      letter-spacing: .02em;
    }

    .section-title:first-child {
      margin-top: 2px;
    }

    .cards {
      display: grid;
      grid-template-columns: repeat(auto-fill, minmax(92px, 1fr));
      gap: 6px;
    }

    .card {
      align-items: center;
      justify-content: center;
      min-height: 30px;
      padding: 4px 6px;
      font-size: var(--fontSizeBase200);
      line-height: 1.3;
    }

    .card svg {
      width: 13px;
      height: 13px;
      flex: none;
      opacity: .85;
    }

    .sub-card {
      display: flex;
      align-items: center;
      gap: var(--spacingHorizontalXS);
      width: 100%;
      min-height: 34px;
      margin-bottom: var(--spacingVerticalXS);
      padding: var(--spacingVerticalXS) var(--spacingHorizontalS);
      border: var(--strokeWidthThin) solid var(--colorNeutralStroke1);
      border-radius: var(--borderRadiusMedium);
      background: var(--colorNeutralBackground1);
      color: var(--colorNeutralForeground1);
      font-size: var(--fontSizeBase200);
      text-align: left;
      cursor: pointer;
    }

    .sub-card:hover {
      background: var(--colorNeutralBackground1Hover);
    }

    .sub-card .meta {
      display: block;
      color: var(--colorNeutralForeground3);
      font-size: var(--fontSizeBase100);
    }

    .sub-card:focus-visible {
      outline: var(--strokeWidthThicker) solid var(--colorStrokeFocus2);
      outline-offset: -1px;
    }

    .empty {
      padding: var(--spacingVerticalL) var(--spacingHorizontalS);
      color: var(--colorNeutralForeground3);
      font-size: var(--fontSizeBase200);
      line-height: 1.45;
      text-align: center;
    }

    .footer {
      flex: none;
      display: grid;
      gap: var(--spacingVerticalXS);
      padding: var(--spacingVerticalS) var(--spacingHorizontalS);
      border-top: var(--strokeWidthThin) solid var(--colorNeutralStroke2);
    }

    .footer .hint {
      font-size: var(--fontSizeBase100);
      line-height: 1.35;
    }

    .record {
      display: flex;
      align-items: center;
      justify-content: center;
      gap: var(--spacingHorizontalS);
      min-height: 30px;
      border: var(--strokeWidthThin) solid var(--colorNeutralStroke1);
      border-radius: var(--borderRadiusMedium);
      background: var(--colorNeutralBackground1);
      color: var(--colorNeutralForeground1);
      font-size: var(--fontSizeBase200);
      cursor: pointer;
    }

    .record:hover {
      background: var(--colorNeutralBackground1Hover);
    }

    .record:focus-visible {
      outline: var(--strokeWidthThicker) solid var(--colorStrokeFocus2);
      outline-offset: -1px;
    }

    .record[disabled] {
      color: var(--colorNeutralForegroundDisabled);
      cursor: default;
    }

    .record-dot,
    .record-stop {
      width: 10px;
      height: 10px;
      flex: none;
    }

    .record-dot {
      border-radius: 50%;
      background: var(--colorPaletteRedBackground3);
    }

    .record-stop {
      background: var(--colorNeutralForegroundOnBrand);
    }

    .record[data-recording='true'] {
      border-color: var(--colorPaletteRedBorder2);
      background: var(--colorPaletteRedBackground3);
      color: var(--colorNeutralForegroundOnBrand);
    }

    .record[data-recording='true'] .record-stop {
      animation: record-pulse 1.2s ease-in-out infinite alternate;
    }

    .record-count {
      font-size: var(--fontSizeBase100);
      opacity: .85;
    }

    @keyframes record-pulse {
      from { opacity: 1; }
      to { opacity: .35; }
    }

    @media (prefers-reduced-motion: reduce) {
      .record[data-recording='true'] .record-stop {
        animation: none;
      }
    }
  `;
