import { css } from 'lit';

export const stepTreeCss = css`
    :host {
      display: flex;
      flex-direction: column;
      min-height: 0;
      min-width: 0;
      height: 100%;
      background: var(--colorNeutralBackground1);
    }

    .notices {
      flex: none;
      display: grid;
      gap: 2px;
      padding: var(--spacingVerticalXS) var(--spacingHorizontalL);
      border-bottom: var(--strokeWidthThin) solid var(--colorNeutralStroke2);
    }

    .notice {
      margin: 0;
      color: var(--colorNeutralForeground2);
      font-size: var(--fontSizeBase200);
      line-height: 1.4;
      white-space: pre-line;
    }

    .notice.warning {
      color: var(--colorStatusWarningForeground1);
    }

    .scroll {
      flex: 1;
      min-height: 0;
      overflow: auto;
      padding: var(--spacingVerticalS) var(--spacingHorizontalL) var(--spacingVerticalXXL);
    }

    .empty {
      display: grid;
      place-content: center;
      gap: var(--spacingVerticalXS);
      height: 100%;
      min-height: 180px;
      padding: var(--spacingVerticalXXL);
      color: var(--colorNeutralForeground3);
      text-align: center;
      font-size: var(--fontSizeBase300);
    }

    .empty .hint {
      font-size: var(--fontSizeBase200);
    }

    .list {
      display: grid;
      gap: 1px;
      margin: 0;
      padding: 0;
      list-style: none;
    }

    .step {
      display: flex;
      flex-direction: column;
      min-width: 0;
    }

    .row {
      display: grid;
      grid-template-columns: 18px 16px 16px minmax(0, max-content) max-content minmax(0, 1fr) auto auto;
      align-items: center;
      gap: 5px;
      min-height: 30px;
      padding: 2px 6px;
      border-left: 2px solid transparent;
      border-radius: var(--borderRadiusMedium);
      color: var(--colorNeutralForeground1);
      cursor: default;
      outline: none;
    }

    .row:hover {
      background: var(--colorSubtleBackgroundHover);
    }

    .row[data-selected='true'] {
      background: var(--colorBrandBackground2);
      border-left-color: var(--colorBrandStroke1);
    }

    .row:focus-visible {
      outline: var(--strokeWidthThicker) solid var(--colorStrokeFocus2);
      outline-offset: -2px;
    }

    .row[data-problem='true'] {
      border-left-color: var(--colorStatusWarningBorder2);
    }

    .row[data-skipped='true'] {
      opacity: .45;
    }

    .twisty {
      width: 18px;
      height: 18px;
    }

    .twisty svg {
      width: 12px;
      height: 12px;
    }

    .twisty.placeholder {
      visibility: hidden;
    }

    .kind {
      display: grid;
      place-items: center;
      color: var(--colorNeutralForeground2);
    }

    .kind svg {
      width: 15px;
      height: 15px;
    }

    .enabled {
      width: 13px;
      height: 13px;
      margin: 0;
      accent-color: var(--colorBrandBackground);
      cursor: pointer;
    }

    .name {
      min-width: 0;
      max-width: 260px;
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
      font-size: var(--fontSizeBase300);
    }

    .badges {
      display: flex;
      align-items: center;
      gap: 4px;
      min-width: 0;
      flex: none;
    }

    .badge {
      display: inline-flex;
      align-items: center;
      height: 17px;
      padding: 0 5px;
      border-radius: var(--borderRadiusSmall);
      font-size: var(--fontSizeBase100);
      line-height: 1;
      white-space: nowrap;
    }

    .badge.id {
      background: var(--colorBrandBackground2);
      color: var(--colorBrandForeground2);
      font-family: var(--fontFamilyMonospace);
    }

    .badge.skip {
      background: var(--colorStatusWarningBackground1);
      color: var(--colorStatusWarningForeground1);
    }

    .badge.input {
      background: var(--colorNeutralBackground2);
      color: var(--colorNeutralForeground2);
    }

    .detail {
      min-width: 0;
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
      color: var(--colorNeutralForeground3);
      font-size: var(--fontSizeBase200);
    }

    .problem-mark {
      display: grid;
      place-items: center;
      color: var(--colorStatusWarningForeground1);
    }

    .problem-mark svg {
      width: 14px;
      height: 14px;
    }

    .row-actions {
      display: flex;
      align-items: center;
      gap: 1px;
      opacity: 0;
    }

    .row:hover .row-actions,
    .row[data-selected='true'] .row-actions,
    .row:focus-within .row-actions {
      opacity: 1;
    }

    .row-actions .ghost {
      width: 22px;
      height: 22px;
    }

    .row-actions .ghost svg {
      width: 12px;
      height: 12px;
    }

    .expanded {
      display: grid;
      gap: 2px;
      margin: 0 0 3px 39px;
      padding: 3px 8px 5px;
      border-left: 2px solid var(--colorBrandBackground2);
      color: var(--colorNeutralForeground2);
      font-size: var(--fontSizeBase200);
    }

    .expanded .line {
      white-space: pre-wrap;
      overflow-wrap: anywhere;
    }

    .expanded .comment {
      color: var(--colorNeutralForeground1);
    }

    .expanded .meta {
      color: var(--colorNeutralForeground3);
    }

    .sections {
      display: grid;
      gap: 1px;
      margin: 1px 0 4px 14px;
      padding-left: 8px;
      border-left: 1px dashed var(--colorNeutralStroke2);
      position: relative;
    }

    .sections > .section-head,
    .sections > .section-empty,
    .sections > .list > .step {
      position: relative;
    }

    .sections > .section-head::before,
    .sections > .section-empty::before,
    .sections > .list > .step::before {
      content: '';
      position: absolute;
      left: -9px;
      top: 15px;
      width: 9px;
      border-top: 1px dashed var(--colorNeutralStroke2);
      pointer-events: none;
    }

    .sections > .section-empty::before {
      top: 9px;
    }

    .section-head {
      display: flex;
      align-items: center;
      gap: var(--spacingHorizontalS);
      min-height: 24px;
      color: var(--colorNeutralForeground3);
      font-size: var(--fontSizeBase100);
    }

    .section-head .label {
      font-weight: var(--fontWeightSemibold);
    }

    .section-head .count {
      opacity: .8;
    }

    .section-add {
      margin-left: auto;
      width: auto;
      height: 22px;
      padding: 0 6px;
      gap: 2px;
      display: inline-flex;
      align-items: center;
      color: var(--colorNeutralForeground2);
      font-size: var(--fontSizeBase100);
    }

    .section-add svg {
      width: 11px;
      height: 11px;
    }

    .section-add:disabled {
      color: var(--colorNeutralForegroundDisabled);
    }

    .section-empty {
      padding: 2px 0 2px 24px;
      color: var(--colorNeutralForeground3);
      font-size: var(--fontSizeBase100);
    }

    .block > .row {
      background: var(--colorNeutralBackground2);
      border: var(--strokeWidthThin) solid var(--colorNeutralStroke2);
      border-left-width: 2px;
      border-radius: var(--borderRadiusLarge);
      padding: 4px 6px;
    }

    .block > .row:hover {
      background: var(--colorNeutralBackground2Hover);
    }

    .block > .row[data-selected='true'] {
      background: var(--colorBrandBackground2);
      border-color: var(--colorBrandStroke1);
    }
    .row[data-drop='before'] {
      box-shadow: inset 0 2px 0 var(--colorBrandStroke1);
    }

    .row[data-drop='after'] {
      box-shadow: inset 0 -2px 0 var(--colorBrandStroke1);
    }

    .menu-backdrop {
      position: fixed;
      inset: 0;
      z-index: 40;
    }

    .menu {
      position: fixed;
      z-index: 41;
      min-width: 196px;
      padding: 4px;
      background: var(--colorNeutralBackground1);
      border: var(--strokeWidthThin) solid var(--colorNeutralStroke1);
      border-radius: var(--borderRadiusMedium);
      box-shadow: var(--shadow16);
    }

    .menu button {
      display: flex;
      align-items: center;
      gap: var(--spacingHorizontalS);
      width: 100%;
      height: 30px;
      padding: 0 var(--spacingHorizontalS);
      border: none;
      border-radius: var(--borderRadiusSmall);
      background: transparent;
      color: var(--colorNeutralForeground1);
      font-size: var(--fontSizeBase200);
      text-align: left;
      cursor: default;
    }

    .menu button:hover:not(:disabled) {
      background: var(--colorSubtleBackgroundHover);
    }

    .menu button:disabled {
      color: var(--colorNeutralForegroundDisabled);
    }

    .menu button svg {
      width: 14px;
      height: 14px;
      flex: none;
    }

    .menu .sep {
      height: 1px;
      margin: 4px;
      background: var(--colorNeutralStroke2);
    }
  `;
