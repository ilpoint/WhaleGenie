import { css } from 'lit';

/** The wizard's own words and spacing: Fluent tokens only, like every page in the shell. */
export const transferDialogCss = css`
    :host {
      display: contents;
    }

    .overlay {
      position: fixed;
      inset: 0;
      z-index: 90;
      display: grid;
      place-items: center;
      padding: var(--spacingVerticalXL);
      background: var(--shell-scrim);
    }

    .dialog {
      display: grid;
      grid-template-rows: auto 1fr auto;
      gap: var(--spacingVerticalM);
      width: min(560px, 100%);
      max-height: min(640px, 100%);
      min-height: 0;
      padding: var(--spacingVerticalL);
      border: var(--strokeWidthThin) solid var(--colorNeutralStroke1);
      border-radius: var(--borderRadiusLarge);
      background: var(--colorNeutralBackground1);
      box-shadow: var(--shadow16);
    }

    h2 {
      margin: 0;
      font-size: var(--fontSizeBase400);
      font-weight: var(--fontWeightSemibold);
    }

    .hint,
    .meta,
    .count {
      margin: 0;
      color: var(--colorNeutralForeground3);
      font-size: var(--fontSizeBase200);
    }

    .rows,
    .conflicts {
      display: grid;
      align-content: start;
      gap: 2px;
      min-height: 0;
      overflow: auto;
    }

    .row {
      display: flex;
      align-items: center;
      gap: var(--spacingHorizontalS);
      min-height: 30px;
      padding: var(--spacingVerticalXS) var(--spacingHorizontalS);
      border-radius: var(--borderRadiusMedium);
    }

    .row:hover {
      background: var(--colorNeutralBackground1Hover);
    }

    .row input {
      margin: 0;
    }

    .row .name {
      flex: 1;
      min-width: 0;
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
    }

    .row[data-covered='true'] .name {
      color: var(--colorNeutralForeground3);
    }

    .row[data-sub='true'] {
      padding-left: calc(var(--spacingHorizontalM) + var(--spacingHorizontalL));
    }

    .problem,
    .error {
      margin: 0;
      color: var(--colorPaletteRedForeground1);
      font-size: var(--fontSizeBase200);
    }

    .conflict {
      display: grid;
      grid-template-columns: minmax(0, 1fr) 180px;
      align-items: center;
      gap: var(--spacingHorizontalS);
    }

    .conflict select {
      font: inherit;
      width: 100%;
    }

    .footer {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      justify-content: flex-end;
      gap: var(--spacingHorizontalS);
    }

    .footer .count {
      margin-right: auto;
    }

    .report {
      margin: 0;
      white-space: pre-line;
      color: var(--colorNeutralForeground1);
    }

    .preview {
      display: grid;
      gap: var(--spacingVerticalS);
      padding: var(--spacingVerticalS);
      border: var(--strokeWidthThin) solid var(--colorNeutralStroke2);
      border-radius: var(--borderRadiusMedium);
      background: var(--colorNeutralBackground2);
    }

    .preview-head,
    .preview-entry {
      display: flex;
      flex-wrap: wrap;
      align-items: baseline;
      gap: var(--spacingHorizontalS);
    }

    .preview-head .name,
    .preview-entry .name {
      font-weight: var(--fontWeightSemibold);
    }

    .preview-columns {
      display: grid;
      grid-template-columns: minmax(0, 1fr) minmax(0, 1fr);
      gap: var(--spacingHorizontalM);
    }

    .preview-columns h3 {
      margin: 0 0 var(--spacingVerticalXS);
      font-size: var(--fontSizeBase300);
      font-weight: var(--fontWeightSemibold);
    }

    .preview-columns p,
    .preview-entry {
      margin: 0;
    }

    @media (max-width: 520px) {
      .preview-columns {
        grid-template-columns: 1fr;
      }
    }
 `;
