import { css } from 'lit';

export const macrosPageCss = css`
    :host {
      display: flex;
      flex-direction: column;
      height: 100%;
    }

    .filters {
      display: flex;
      flex-direction: column;
      gap: var(--spacingVerticalS);
      padding: var(--spacingVerticalM) var(--spacingHorizontalXL);
      border-bottom: var(--strokeWidthThin) solid var(--colorNeutralStroke1);
    }

    .filebar {
      display: flex;
      align-items: center;
      gap: var(--spacingHorizontalSNudge);
      padding: var(--spacingVerticalS) var(--spacingHorizontalXL) 0;
    }

    .filebar .path {
      color: var(--colorNeutralForeground4);
      font-size: var(--fontSizeBase200);
      white-space: nowrap;
      overflow: hidden;
      text-overflow: ellipsis;
      min-width: 0;
      flex: 1;
      text-align: right;
    }

    .save-error {
      margin: 0;
      padding: var(--spacingVerticalS) var(--spacingHorizontalXL);
      color: var(--colorPaletteRedForeground1);
      background: var(--colorPaletteRedBackground1);
      font-size: var(--fontSizeBase200);
    }

    .dock {
      display: flex;
      flex-wrap: wrap;
      gap: var(--spacingHorizontalSNudge);
    }

    .dock button {
      border: none;
      border-radius: var(--borderRadiusMedium);
      background: transparent;
      color: var(--colorNeutralForeground1);
      font: inherit;
      padding: var(--spacingVerticalSNudge) var(--spacingHorizontalM);
      cursor: pointer;
    }

    .dock button:hover {
      background: var(--colorNeutralBackground1Hover);
    }

    .dock button[aria-current='true'] {
      background: var(--colorBrandBackground);
      color: var(--colorNeutralForegroundOnBrand);
    }

    .dock button:focus-visible,
    .tools input:focus-visible {
      outline: var(--strokeWidthThicker) solid var(--colorStrokeFocus2);
      outline-offset: -1px;
    }

    .tools {
      display: flex;
      align-items: center;
      gap: var(--spacingHorizontalM);
      flex-wrap: wrap;
    }

    .tools input {
      flex: 1;
      min-width: 160px;
      max-width: 320px;
      border: var(--strokeWidthThin) solid var(--colorNeutralStroke1);
      border-bottom: var(--strokeWidthThicker) solid var(--colorNeutralStrokeAccessible);
      border-radius: var(--borderRadiusMedium);
      background: var(--colorNeutralBackground1);
      color: var(--colorNeutralForeground1);
      font: inherit;
      padding: var(--spacingVerticalSNudge) var(--spacingHorizontalM);
    }

    .tools input::placeholder {
      color: var(--colorNeutralForeground4);
    }

    .list {
      flex: 1;
      overflow: auto;
      display: flex;
      flex-direction: column;
      gap: var(--spacingVerticalSNudge);
      padding: var(--spacingVerticalM) var(--spacingHorizontalXL);
    }

    .card {
      display: grid;
      grid-template-columns: auto auto 1fr auto auto;
      align-items: center;
      gap: var(--spacingHorizontalM);
      background: var(--colorNeutralBackground1);
      border: var(--strokeWidthThin) solid var(--colorNeutralStroke1);
      border-radius: var(--borderRadiusMedium);
      padding: var(--spacingVerticalM) var(--spacingHorizontalL);
    }

    .card.dragging {
      opacity: 0.45;
    }

    .card[data-drop='before'] {
      box-shadow: inset 0 2px 0 var(--colorBrandStroke1);
    }

    .card[data-drop='after'] {
      box-shadow: inset 0 -2px 0 var(--colorBrandStroke1);
    }

    .drag-handle {
      width: 18px;
      height: 32px;
      display: grid;
      place-items: center;
      border: none;
      border-radius: var(--borderRadiusMedium);
      background: transparent;
      color: var(--colorNeutralForeground3);
      cursor: grab;
      padding: 0;
      touch-action: none;
    }

    .drag-handle:hover {
      background: var(--colorNeutralBackground1Hover);
      color: var(--colorNeutralForeground1);
    }

    .drag-handle:focus-visible {
      outline: var(--strokeWidthThicker) solid var(--colorStrokeFocus2);
      outline-offset: -1px;
    }

    .dot {
      width: 10px;
      height: 10px;
      border-radius: 50%;
      background: var(--colorNeutralStrokeAccessible);
    }

    .dot[data-enabled='true'] {
      background: var(--colorBrandForeground1);
    }

    .card .body {
      display: flex;
      flex-direction: column;
      gap: var(--spacingVerticalXS);
      min-width: 0;
    }

    .card .name {
      font-weight: var(--fontWeightSemibold);
      display: flex;
      align-items: center;
      gap: var(--spacingHorizontalS);
    }

    .card .name .group {
      color: var(--colorNeutralForeground3);
      font-weight: var(--fontWeightRegular);
    }

    .card .triggers {
      color: var(--colorNeutralForeground2);
      font-size: var(--fontSizeBase200);
    }

    .card fluent-switch {
      justify-self: end;
    }

    .iconbuttons {
      display: flex;
      gap: var(--spacingHorizontalXS);
    }

    .iconbuttons button {
      width: 32px;
      height: 32px;
      display: grid;
      place-items: center;
      border: none;
      border-radius: var(--borderRadiusMedium);
      background: transparent;
      color: var(--colorNeutralForeground2);
      cursor: pointer;
      padding: 0;
    }

    .iconbuttons button:hover {
      background: var(--colorNeutralBackground1Hover);
      color: var(--colorNeutralForeground1);
    }

    .iconbuttons button:focus-visible {
      outline: var(--strokeWidthThicker) solid var(--colorStrokeFocus2);
      outline-offset: -1px;
    }

    .iconbuttons button.danger:hover {
      background: var(--colorPaletteRedBackground1);
      color: var(--colorPaletteRedForeground1);
    }

    .iconbuttons svg {
      width: 16px;
      height: 16px;
    }

    .empty {
      flex: 1;
      display: flex;
      flex-direction: column;
      align-items: center;
      justify-content: center;
      gap: var(--spacingVerticalS);
      color: var(--colorNeutralForeground3);
      text-align: center;
      padding: var(--spacingVerticalXXL);
    }

  `;
