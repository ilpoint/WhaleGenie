import { css } from 'lit';

export const settingsPageCss = css`
    :host {
      display: grid;
      grid-template-columns: 220px minmax(0, 1fr);
      grid-template-rows: auto 1fr;
      height: 100%;
      min-height: 0;
      overflow: hidden;
      background: var(--colorNeutralBackground1);
    }

    .header {
      grid-column: 1 / -1;
      display: flex;
      align-items: center;
      gap: var(--spacingHorizontalM);
      padding: var(--spacingVerticalS) var(--spacingHorizontalL);
      border-bottom: var(--strokeWidthThin) solid var(--colorNeutralStroke2);
      background: var(--colorNeutralBackground1);
    }

    .header h1 {
      margin: 0;
      flex: 1;
      font-size: var(--fontSizeBase400);
      font-weight: var(--fontWeightSemibold);
    }

    .header input {
      width: min(340px, 42vw);
      font: inherit;
    }

    .rail {
      min-height: 0;
      overflow: auto;
      padding: var(--spacingVerticalS) var(--spacingHorizontalXS);
      border-right: var(--strokeWidthThin) solid var(--colorNeutralStroke2);
      background: var(--colorNeutralBackground2);
    }

    .rail button {
      display: block;
      width: 100%;
      padding: var(--spacingVerticalS) var(--spacingHorizontalM);
      border: 0;
      border-radius: var(--borderRadiusMedium);
      background: transparent;
      color: var(--colorNeutralForeground1);
      font: inherit;
      text-align: left;
      cursor: pointer;
    }

    .rail button:hover { background: var(--colorNeutralBackground2Hover); }

    .content {
      min-height: 0;
      overflow: auto;
      padding: var(--spacingVerticalL) var(--spacingHorizontalXXL);
    }

    .content h1 {
      margin: 0 0 var(--spacingVerticalL);
      font-size: var(--fontSizeBase500);
      font-weight: var(--fontWeightSemibold);
    }

    h3 {
      margin: var(--spacingVerticalM) 0 0;
      font-size: var(--fontSizeBase300);
    }

    .cards {
      display: grid;
      gap: var(--spacingVerticalM);
    }

    .card {
      display: grid;
      gap: var(--spacingVerticalS);
      padding: var(--spacingVerticalM) var(--spacingHorizontalM);
      border: var(--strokeWidthThin) solid var(--colorNeutralStroke2);
      border-radius: var(--borderRadiusLarge);
      background: var(--colorNeutralBackground1);
    }

    .card-head {
      display: flex;
      flex-wrap: wrap;
      align-items: baseline;
      gap: var(--spacingHorizontalS);
    }

    .card h2,
    .slot-editor h3 {
      margin: 0;
      font-size: var(--fontSizeBase400);
    }

    .note {
      color: var(--colorNeutralForeground3);
      font-size: var(--fontSizeBase200);
    }

    .row {
      display: grid;
      grid-template-columns: minmax(0, 1fr) 180px;
      gap: var(--spacingHorizontalM);
      align-items: center;
    }

    .row label,
    .field > span:first-child {
      color: var(--colorNeutralForeground1);
    }

    .settings-grid {
      display: grid;
      grid-template-columns: repeat(auto-fit, minmax(240px, 1fr));
      gap: var(--spacingHorizontalM) var(--spacingHorizontalL);
      align-items: end;
    }

    .settings-grid.compact {
      grid-template-columns: repeat(auto-fit, minmax(210px, 1fr));
      gap: var(--spacingVerticalS) var(--spacingHorizontalM);
      align-items: center;
    }

    .field {
      display: grid;
      gap: var(--spacingVerticalXS);
      min-width: 0;
    }

    .field select,
    .field input {
      width: 100%;
      max-width: 280px;
      font: inherit;
    }

    .field .inline-unit {
      max-width: 220px;
    }

    .inline-unit {
      display: flex;
      align-items: center;
      gap: var(--spacingHorizontalXS);
    }

    .inline-unit input {
      width: 100%;
    }

    .inline-unit em {
      color: var(--colorNeutralForeground3);
      font-size: var(--fontSizeBase200);
      font-style: normal;
      white-space: nowrap;
    }

    .check-row {
      display: flex;
      align-items: center;
      gap: var(--spacingHorizontalS);
      min-height: 28px;
      color: var(--colorNeutralForeground1);
    }

    .check-row input {
      width: auto;
    }

    .hotkey-row {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: var(--spacingHorizontalS);
    }

    .binding {
      display: inline-flex;
      align-items: center;
      min-height: 28px;
      padding: 0 var(--spacingHorizontalS);
      border: var(--strokeWidthThin) solid var(--colorNeutralStroke2);
      border-radius: var(--borderRadiusSmall);
      background: var(--colorNeutralBackground2);
      font-family: var(--fontFamilyMonospace);
      font-size: var(--fontSizeBase200);
    }

    .capture-target {
      min-height: 30px;
      padding: 0 var(--spacingHorizontalM);
      border: var(--strokeWidthThick) solid var(--colorBrandStroke1);
      border-radius: var(--borderRadiusMedium);
      background: var(--colorBrandBackground2);
      color: var(--colorNeutralForeground1);
      font: inherit;
      cursor: default;
    }

    .hint,
    .status {
      margin: 0;
      color: var(--colorNeutralForeground2);
      font-size: var(--fontSizeBase200);
      line-height: 1.45;
    }

    .status {
      color: var(--colorNeutralForeground3);
    }

    .error {
      color: var(--colorPaletteRedForeground1);
      margin: 0;
    }

    .actions {
      display: flex;
      flex-wrap: wrap;
      gap: var(--spacingHorizontalS);
    }

    .rule-list {
      display: grid;
      gap: var(--spacingVerticalXS);
    }

    .rule {
      display: flex;
      align-items: center;
      justify-content: space-between;
      gap: var(--spacingHorizontalM);
      padding: var(--spacingVerticalS) var(--spacingHorizontalS);
      border: var(--strokeWidthThin) solid var(--colorNeutralStroke2);
      border-radius: var(--borderRadiusMedium);
      background: var(--colorNeutralBackground2);
    }

    .rule[draggable='true'] {
      cursor: grab;
    }

    .rule-text {
      display: grid;
      gap: 2px;
      min-width: 0;
    }

    .rule-text strong {
      font-size: var(--fontSizeBase300);
      font-weight: var(--fontWeightSemibold);
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
    }

    .rule-text span {
      color: var(--colorNeutralForeground3);
      font-size: var(--fontSizeBase200);
    }

    .rule-buttons {
      display: flex;
      flex-wrap: wrap;
      justify-content: flex-end;
      gap: 2px;
    }

    .storage-row {
      display: grid;
      grid-template-columns: minmax(0, 1fr) auto;
      gap: var(--spacingHorizontalM);
      align-items: center;
      padding: var(--spacingVerticalXS) 0;
      border-top: var(--strokeWidthThin) solid var(--colorNeutralStroke2);
    }

    .storage-row .where {
      color: var(--colorNeutralForeground3);
      font-size: var(--fontSizeBase200);
      word-break: break-all;
    }

    .wheel-layout {
      display: grid;
      grid-template-columns: minmax(220px, 0.7fr) minmax(320px, 1.3fr);
      gap: var(--spacingHorizontalL);
      align-items: start;
    }

    .scene-panel,
    .wheel-stage {
      display: grid;
      gap: var(--spacingVerticalS);
      min-width: 0;
    }

    .wheel {
      position: relative;
      justify-self: center;
      flex: none;
    }

    .wheel-rings {
      position: absolute;
      inset: 0;
      width: 100%;
      height: 100%;
      overflow: visible;
    }

    .wheel-rings circle {
      fill: none;
      stroke: var(--colorNeutralStroke1);
      stroke-width: 1;
    }

    .wheel-slot {
      position: absolute;
      transform: translate(-50%, -50%);
      min-width: 34px;
      max-width: 84px;
      min-height: 24px;
      padding: 2px 4px;
      border: var(--strokeWidthThin) solid var(--colorNeutralStroke2);
      border-radius: var(--borderRadiusSmall);
      background: var(--colorNeutralBackground2);
      color: var(--colorNeutralForeground1);
      font: inherit;
      line-height: 1.15;
      text-align: center;
      overflow: hidden;
      cursor: default;
    }

    .wheel-slot[data-tone='override'] {
      border-color: var(--colorBrandStroke1);
      background: var(--colorBrandBackground2);
    }

    .wheel-slot[data-tone='extended'] {
      background: var(--colorNeutralBackground4);
    }

    .wheel-slot[data-tone='empty'] {
      border-color: transparent;
      background: transparent;
      color: var(--colorNeutralForeground4);
      opacity: 0.16;
    }

    .wheel-slot[data-tone='empty']:hover,
    .wheel-slot[data-tone='empty'][data-selected='true'] {
      border-color: var(--colorNeutralStroke2);
      background: var(--colorNeutralBackground2);
      opacity: 1;
    }

    .wheel-slot[data-selected='true'] {
      outline: var(--strokeWidthThicker) solid var(--colorBrandStroke1);
      outline-offset: 1px;
    }

    .wheel-slot:disabled {
      cursor: default;
    }

    .wheel-slot .slot-label {
      display: block;
      max-width: 76px;
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
    }

    .wheel-slot .empty {
      font-size: var(--fontSizeBase400);
      line-height: 1;
    }

    .wheel-centre {
      position: absolute;
      left: 50%;
      top: 50%;
      width: 30%;
      transform: translate(-50%, -50%);
      text-align: center;
      color: var(--colorNeutralForeground3);
      font-size: var(--fontSizeBase100);
      overflow: hidden;
      text-overflow: ellipsis;
      pointer-events: none;
    }

    .legend {
      display: flex;
      flex-wrap: wrap;
      gap: var(--spacingHorizontalM);
      font-size: var(--fontSizeBase200);
      color: var(--colorNeutralForeground2);
    }

    .legend span::before {
      content: '';
      display: inline-block;
      width: 10px;
      height: 10px;
      margin-right: 5px;
      border: var(--strokeWidthThin) solid var(--colorNeutralStroke2);
      background: var(--colorNeutralBackground2);
      vertical-align: -1px;
    }

    .legend span[data-tone='override']::before {
      border-color: var(--colorBrandStroke1);
      background: var(--colorBrandBackground2);
    }

    .legend span[data-tone='extended']::before {
      background: var(--colorNeutralBackground4);
    }

    .slot-editor {
      display: grid;
      gap: var(--spacingVerticalS);
      padding: var(--spacingVerticalM);
      border: var(--strokeWidthThin) solid var(--colorNeutralStroke2);
      border-radius: var(--borderRadiusLarge);
      background: var(--colorNeutralBackground2);
    }

    .icon-row,
    .accent-row {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: var(--spacingHorizontalS);
    }

    .icon-row .binding,
    .accent-row .binding {
      flex: 1;
      min-width: 0;
      overflow: hidden;
      text-overflow: ellipsis;
    }

    .accent-row input[type='color'] {
      width: 52px;
      height: 30px;
      padding: 2px;
    }

    .theme-previews {
      display: grid;
      grid-template-columns: repeat(auto-fit, minmax(140px, 1fr));
      gap: var(--spacingHorizontalS);
    }

    .preview {
      min-height: 44px;
      display: grid;
      place-items: center;
      border: var(--strokeWidthThin) solid var(--colorNeutralStroke2);
      border-radius: var(--borderRadiusMedium);
      font-size: var(--fontSizeBase200);
    }
  `;
