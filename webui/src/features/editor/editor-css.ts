import { css } from 'lit';

/**
 * The editor's shared control vocabulary. Every colour and distance comes from a Fluent CSS
 * custom property; the only local names are layout metrics (palette width, inspector width), which
 * are not colours and do not constitute a second theme.
 */
export const editorBase = css`
  :host {
    box-sizing: border-box;
    color: var(--colorNeutralForeground1);
    font-family: var(--fontFamilyBase);
    font-size: var(--fontSizeBase300);
  }

  *,
  *::before,
  *::after {
    box-sizing: border-box;
  }

  button {
    font: inherit;
    color: inherit;
  }

  .dock {
    display: flex;
    flex-wrap: wrap;
    gap: var(--spacingHorizontalXS);
  }

  .dock button {
    border: var(--strokeWidthThin) solid transparent;
    border-radius: var(--borderRadiusMedium);
    background: transparent;
    color: var(--colorNeutralForeground2);
    padding: var(--spacingVerticalXS) var(--spacingHorizontalS);
    cursor: pointer;
  }

  .dock button:hover {
    background: var(--colorSubtleBackgroundHover);
    color: var(--colorNeutralForeground1);
  }

  .dock button:active {
    background: var(--colorSubtleBackgroundPressed);
  }

  .dock button[aria-selected='true'],
  .dock button[aria-pressed='true'] {
    background: var(--colorBrandBackground2);
    border-color: var(--colorBrandStroke1);
    color: var(--colorBrandForeground2);
  }

  .dock button:focus-visible {
    outline: var(--strokeWidthThicker) solid var(--colorStrokeFocus2);
    outline-offset: -1px;
  }

  .chip {
    display: flex;
    align-items: center;
    justify-content: center;
    gap: var(--spacingHorizontalXS);
    min-height: 28px;
    padding: var(--spacingVerticalXS) var(--spacingHorizontalS);
    border: var(--strokeWidthThin) solid var(--colorNeutralStroke1);
    border-radius: var(--borderRadiusMedium);
    background: var(--colorNeutralBackground1);
    color: var(--colorNeutralForeground1);
    font: inherit;
    font-size: var(--fontSizeBase200);
    line-height: 1.35;
    text-align: center;
    cursor: pointer;
    overflow-wrap: anywhere;
  }

  .chip:hover {
    background: var(--colorNeutralBackground1Hover);
  }

  .chip:active {
    background: var(--colorNeutralBackground1Pressed);
  }

  .chip[aria-pressed='true'],
  .chip[aria-selected='true'] {
    background: var(--colorBrandBackground2);
    border-color: var(--colorBrandStroke1);
    color: var(--colorBrandForeground2);
  }

  .chip:focus-visible {
    outline: var(--strokeWidthThicker) solid var(--colorStrokeFocus2);
    outline-offset: -1px;
  }

  .ghost {
    display: grid;
    place-items: center;
    width: 24px;
    height: 24px;
    padding: 0;
    border: none;
    border-radius: var(--borderRadiusSmall);
    background: transparent;
    color: var(--colorNeutralForeground2);
    cursor: pointer;
  }

  .ghost:hover {
    background: var(--colorSubtleBackgroundHover);
    color: var(--colorNeutralForeground1);
  }

  .ghost:active {
    background: var(--colorSubtleBackgroundPressed);
  }

  .ghost:focus-visible {
    outline: var(--strokeWidthThicker) solid var(--colorStrokeFocus2);
    outline-offset: -1px;
  }

  .ghost[disabled] {
    color: var(--colorNeutralForegroundDisabled);
    cursor: default;
  }

  .ghost svg {
    width: 14px;
    height: 14px;
  }

  .field {
    display: grid;
    gap: var(--spacingVerticalXS);
    min-width: 0;
  }

  .field > span:first-child,
  .field > label:first-child {
    color: var(--colorNeutralForeground2);
    font-size: var(--fontSizeBase200);
  }

  .field input,
  .field select,
  .field textarea {
    width: 100%;
    min-width: 0;
    font: inherit;
    color: var(--colorNeutralForeground1);
    background: var(--colorNeutralBackground1);
    border: var(--strokeWidthThin) solid var(--colorNeutralStroke1);
    border-bottom: var(--strokeWidthThicker) solid var(--colorNeutralStrokeAccessible);
    border-radius: var(--borderRadiusMedium);
    padding: var(--spacingVerticalXS) var(--spacingHorizontalS);
  }

  .field textarea {
    min-height: 64px;
    resize: vertical;
  }

  .field input:focus-visible,
  .field select:focus-visible,
  .field textarea:focus-visible {
    outline: var(--strokeWidthThicker) solid var(--colorStrokeFocus2);
    outline-offset: -1px;
  }

  .hint {
    margin: 0;
    color: var(--colorNeutralForeground3);
    font-size: var(--fontSizeBase200);
    line-height: 1.4;
  }

  .value-chip {
    min-height: 26px;
    padding: 0 var(--spacingHorizontalS);
    border: var(--strokeWidthThin) solid var(--colorNeutralStroke1);
    border-radius: var(--borderRadiusMedium);
    background: var(--colorNeutralBackground1);
    color: var(--colorNeutralForeground2);
    font-size: var(--fontSizeBase100);
    white-space: nowrap;
    cursor: pointer;
  }

  .value-chip:hover {
    background: var(--colorNeutralBackground1Hover);
    color: var(--colorNeutralForeground1);
  }

  .value-chip:focus-visible {
    outline: var(--strokeWidthThicker) solid var(--colorStrokeFocus2);
    outline-offset: -1px;
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

  .inline {
    display: flex;
    flex-wrap: wrap;
    align-items: center;
    gap: var(--spacingHorizontalS);
  }

  .inline input[type='checkbox'] {
    accent-color: var(--colorBrandBackground);
  }

  .caption {
    color: var(--colorNeutralForeground3);
    font-size: var(--fontSizeBase100);
  }

  .problem {
    margin: 0;
    color: var(--colorStatusWarningForeground1);
    font-size: var(--fontSizeBase200);
    line-height: 1.4;
  }

  .error {
    color: var(--colorStatusDangerForeground1);
  }

  .panel {
    background: var(--colorNeutralBackground1);
    border: var(--strokeWidthThin) solid var(--colorNeutralStroke2);
    border-radius: var(--borderRadiusLarge);
  }

  .sr-only {
    position: absolute;
    width: 1px;
    height: 1px;
    padding: 0;
    margin: -1px;
    overflow: hidden;
    clip: rect(0, 0, 0, 0);
    white-space: nowrap;
    border: 0;
  }
`;
