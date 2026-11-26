import { css } from 'lit';

export const macroSettingsCss = css`
    :host {
      display: flex;
      flex-direction: column;
      min-height: 0;
      overflow: hidden;
    }

    .title {
      display: flex;
      align-items: center;
      gap: var(--spacingHorizontalXS);
      flex: none;
      height: 36px;
      padding: 0 var(--spacingHorizontalL);
      border-bottom: var(--strokeWidthThin) solid var(--colorNeutralStroke2);
      background: var(--colorNeutralBackground2);
      color: var(--colorNeutralForeground1);
    }

    .title h3 {
      margin: 0;
      font-size: var(--fontSizeBase300);
      font-weight: var(--fontWeightSemibold);
    }

    .tabs {
      display: flex;
      flex: none;
      gap: var(--spacingHorizontalXS);
      padding: 0 var(--spacingHorizontalL);
      border-bottom: var(--strokeWidthThin) solid var(--colorNeutralStroke2);
    }

    .body {
      flex: 1;
      min-height: 0;
      overflow: auto;
      padding: var(--spacingVerticalS) var(--spacingHorizontalL) var(--spacingVerticalL);
    }

    .footer {
      display: flex;
      align-items: center;
      gap: var(--spacingHorizontalXS);
      flex: none;
      padding: var(--spacingVerticalS) var(--spacingHorizontalL);
      border-top: var(--strokeWidthThin) solid var(--colorNeutralStroke2);
      background: var(--colorNeutralBackground2);
    }

    .footer .error {
      flex: 1;
      min-width: 0;
      color: var(--colorStatusDangerForeground1);
      font-size: var(--fontSizeBase100);
      overflow: hidden;
      text-overflow: ellipsis;
    }

    .footer fluent-button {
      min-width: 0;
    }

    .project-status {
      flex: none;
      padding: var(--spacingVerticalXS) var(--spacingHorizontalL);
      border-top: var(--strokeWidthThin) solid var(--colorNeutralStroke2);
      background: var(--colorNeutralBackground2);
      color: var(--colorNeutralForeground3);
      font-size: var(--fontSizeBase100);
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
    }

    .project-status[data-project-status='saving'] {
      color: var(--colorBrandForeground1);
    }

    .project-status[data-project-status='error'] {
      color: var(--colorStatusDangerForeground1);
    }

    .project-status[data-project-status='dirty'] {
      color: var(--colorStatusWarningForeground1);
    }

    .tabs button {
      border: none;
      border-bottom: 2px solid transparent;
      background: transparent;
      color: var(--colorNeutralForeground3);
      padding: var(--spacingVerticalXS) var(--spacingHorizontalS);
      cursor: pointer;
    }

    .tabs button[aria-selected='true'] {
      color: var(--colorNeutralForeground1);
      border-bottom-color: var(--colorBrandBackground);
    }

    .tabs button:focus-visible {
      outline: var(--strokeWidthThicker) solid var(--colorStrokeFocus2);
      outline-offset: -1px;
    }

    .page {
      display: grid;
      gap: var(--spacingVerticalS);
      min-width: 0;
    }

    .group {
      display: grid;
      gap: var(--spacingVerticalS);
      padding: var(--spacingVerticalS) var(--spacingHorizontalS);
      border: var(--strokeWidthThin) solid var(--colorNeutralStroke2);
      border-radius: var(--borderRadiusMedium);
      background: var(--colorNeutralBackground2);
    }

    .group > h3 {
      margin: 0;
      color: var(--colorNeutralForeground2);
      font-size: var(--fontSizeBase200);
      font-weight: var(--fontWeightSemibold);
    }

    .triggers {
      display: grid;
      gap: var(--spacingVerticalXS);
    }

    .trigger {
      display: grid;
      grid-template-columns: 28px minmax(0, 1fr) 24px;
      gap: var(--spacingHorizontalXS);
      align-items: center;
      min-height: 36px;
      padding: var(--spacingVerticalXS) var(--spacingHorizontalS);
      border: var(--strokeWidthThin) solid var(--colorNeutralStroke2);
      border-radius: var(--borderRadiusMedium);
      background: var(--colorNeutralBackground1);
      text-align: left;
      cursor: pointer;
    }

    .trigger:hover {
      background: var(--colorNeutralBackground1Hover);
    }

    .trigger[data-selected='true'] {
      border-color: var(--colorBrandStroke1);
      background: var(--colorBrandBackground2);
    }

    .trigger .body {
      display: grid;
      min-width: 0;
    }

    .trigger .kind {
      font-size: var(--fontSizeBase200);
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
    }

    .trigger .summary {
      color: var(--colorNeutralForeground3);
      font-size: var(--fontSizeBase100);
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
    }

    .trigger input {
      accent-color: var(--colorBrandBackground);
    }

    .trigger-fields {
      display: grid;
      gap: var(--spacingVerticalS);
      padding: var(--spacingVerticalS) var(--spacingHorizontalS);
      border-left: 2px solid var(--colorBrandStroke1);
      background: var(--colorNeutralBackground1);
    }

    .two {
      display: grid;
      grid-template-columns: repeat(2, minmax(0, 1fr));
      gap: var(--spacingVerticalS) var(--spacingHorizontalS);
    }

    .inline {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: var(--spacingHorizontalS);
    }

    .inline input[type='checkbox'] {
      accent-color: var(--colorBrandBackground);
    }

    .hint {
      font-size: var(--fontSizeBase100);
    }

    .link-button {
      justify-self: start;
      min-height: 26px;
      padding: 0 var(--spacingHorizontalS);
      border: var(--strokeWidthThin) solid var(--colorNeutralStroke1);
      border-radius: var(--borderRadiusMedium);
      background: var(--colorNeutralBackground1);
      color: var(--colorNeutralForeground1);
      font-size: var(--fontSizeBase100);
      cursor: pointer;
    }

    .link-button:hover {
      background: var(--colorNeutralBackground1Hover);
    }

    .link-button:focus-visible {
      outline: var(--strokeWidthThicker) solid var(--colorStrokeFocus2);
      outline-offset: -1px;
    }

    .declarations {
      display: grid;
      gap: var(--spacingVerticalXS);
    }

    .declaration {
      display: grid;
      gap: var(--spacingVerticalXS);
      padding: var(--spacingVerticalS) var(--spacingHorizontalS);
      border: var(--strokeWidthThin) solid var(--colorNeutralStroke2);
      border-radius: var(--borderRadiusMedium);
      background: var(--colorNeutralBackground1);
    }

    .declaration-head {
      display: grid;
      grid-template-columns: minmax(0, 1fr) 96px 74px 24px 24px 24px;
      gap: 3px;
      align-items: center;
    }

    .declaration-head input,
    .declaration-head select {
      min-width: 0;
      width: 100%;
    }

    .record-fields {
      display: grid;
      gap: 3px;
      padding: var(--spacingVerticalXS) var(--spacingHorizontalS);
      border-left: 2px solid var(--colorNeutralStroke2);
      background: var(--colorNeutralBackground2);
    }

    .record-field {
      display: grid;
      grid-template-columns: minmax(0, 1fr) 100px 24px;
      gap: 3px;
    }

    .empty {
      color: var(--colorNeutralForeground3);
      font-size: var(--fontSizeBase200);
    }

    .saved {
      color: var(--colorStatusSuccessForeground1);
      font-size: var(--fontSizeBase200);
    }

    .end-grid { display: flex; flex-wrap: wrap; gap: 4px; }
    .keycap { min-width: 34px; height: 32px; padding: 0 7px; border: var(--strokeWidthThin) solid var(--colorNeutralStroke1); border-radius: var(--borderRadiusMedium); background: var(--colorNeutralBackground2); color: var(--colorNeutralForeground3); font: inherit; font-size: var(--fontSizeBase200); cursor: pointer; }
    .keycap.on { border-color: var(--colorBrandStroke1); background: var(--colorBrandBackground2); color: var(--colorBrandForeground2); }
    .keycap.custom { border-style: dashed; }
    .keycap:hover { background: var(--colorNeutralBackground2Hover); }
    .key-row { display: flex; gap: var(--spacingHorizontalXS); flex-wrap: wrap; align-items: center; }
    .member-list { display: flex; flex-wrap: wrap; gap: 3px; margin: 0; padding: 0; list-style: none; }
    .member-list li { padding: 1px 6px; border-radius: var(--borderRadiusSmall); background: var(--colorNeutralBackground3); font-size: var(--fontSizeBase100); }
  `;