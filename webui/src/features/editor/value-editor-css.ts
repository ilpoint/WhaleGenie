import { css } from 'lit';

export const valueEditorCss = css`
    :host { position: fixed; inset: 0; z-index: 60; display: grid; place-items: center; padding: 16px; background: var(--shell-scrim); box-sizing: border-box; }
    .dialog { display: flex; flex-direction: column; width: min(1140px, 100%); height: min(720px, 100%); min-height: 0; overflow: hidden; background: var(--colorNeutralBackground1); border: var(--strokeWidthThin) solid var(--colorNeutralStroke1); border-radius: var(--borderRadiusLarge); box-shadow: var(--shadow64); }
    header { display: flex; align-items: flex-start; gap: var(--spacingHorizontalM); padding: var(--spacingVerticalM) var(--spacingHorizontalL) var(--spacingVerticalS); border-bottom: var(--strokeWidthThin) solid var(--colorNeutralStroke2); background: var(--colorNeutralBackground2); }
    header h2 { margin: 0; font-size: var(--fontSizeBase400); font-weight: var(--fontWeightSemibold); }
    header p { margin: 2px 0 0; color: var(--colorNeutralForeground3); font-size: var(--fontSizeBase100); }
    header .close { margin-left: auto; }
    .body { display: flex; flex-direction: column; gap: var(--spacingVerticalS); flex: 1; min-height: 0; padding: var(--spacingVerticalM) var(--spacingHorizontalL); overflow: auto; }
    .band-panel, .panel { border: var(--strokeWidthThin) solid var(--colorNeutralStroke2); border-radius: var(--borderRadiusMedium); background: var(--colorNeutralBackground2); }
    .band-panel { display: flex; flex-direction: column; height: var(--band-height, 280px); min-height: 190px; flex: none; overflow: hidden; }
    .band-splitter { flex: none; height: 6px; cursor: row-resize; border-top: var(--strokeWidthThin) solid var(--colorNeutralStroke2); border-bottom: var(--strokeWidthThin) solid var(--colorNeutralStroke2); background: var(--colorNeutralBackground1); }
    .band-splitter:hover { background: var(--colorBrandBackground2); }
    .band-head { display: flex; align-items: center; gap: var(--spacingHorizontalXS); padding: var(--spacingVerticalXS) var(--spacingHorizontalS); border-bottom: var(--strokeWidthThin) solid var(--colorNeutralStroke2); }
    .band-head .label { font-size: var(--fontSizeBase100); color: var(--colorNeutralForeground2); }
    .band-head .hint { margin-left: auto; color: var(--colorNeutralForeground3); font-size: var(--fontSizeBase100); overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .band { position: relative; flex: 1; min-height: 60px; padding: 10px; overflow: auto; outline: none; }
    .band:focus-visible { box-shadow: inset 0 0 0 2px var(--colorStrokeFocus2); }
    .band .empty-hint { color: var(--colorNeutralForeground3); font-size: var(--fontSizeBase100); font-style: italic; }
    .wrap { display: flex; flex-wrap: wrap; align-items: center; gap: 3px; min-height: 24px; }
    .token { position: relative; display: inline-flex; align-items: center; gap: 3px; margin: 1px; padding: 2px 5px; border: var(--strokeWidthThin) solid var(--colorNeutralStroke2); border-radius: var(--borderRadiusMedium); background: var(--colorNeutralBackground1); color: var(--colorNeutralForeground1); font-family: var(--fontFamilyMonospace); font-size: var(--fontSizeBase200); line-height: 18px; }
    .token.selected { outline: var(--strokeWidthThick) solid var(--colorBrandStroke1); outline-offset: 1px; }
    .token.problem { box-shadow: inset 0 0 0 1px var(--colorPaletteRedBorder2, var(--colorStatusDangerBorder2)); }
    .token.empty { min-width: 28px; min-height: 22px; justify-content: center; border-style: dashed; color: var(--colorNeutralForeground3); font-family: var(--fontFamilyBase); font-size: var(--fontSizeBase100); font-style: italic; }
    .token.empty[data-required='true'] { border-color: var(--colorPaletteRedBorder2, var(--colorStatusDangerBorder2)); }
    .token .call-name { color: var(--colorBrandForeground2); font-weight: var(--fontWeightSemibold); }
    .token .bracket { color: var(--colorNeutralForeground3); }
    .token .op { border: 0; background: transparent; color: var(--colorBrandForeground2); font: inherit; padding: 0 2px; cursor: pointer; }
    .token .remove { display: none; align-items: center; justify-content: center; width: 16px; height: 16px; padding: 0; border: 0; border-radius: var(--borderRadiusSmall); background: var(--colorNeutralBackground3); color: var(--colorNeutralForeground3); cursor: pointer; }
    .token:hover .remove { display: inline-flex; }
    .token .remove:hover { background: var(--colorPaletteRedBackground2, var(--colorStatusDangerBackground2)); color: var(--colorPaletteRedForeground1, var(--colorStatusDangerForeground1)); }
    .token.stack { flex-direction: column; align-items: stretch; gap: 3px; }
    .arg-row { display: flex; align-items: flex-start; gap: 4px; padding-left: 8px; border-left: 1px solid var(--colorNeutralStroke2); }
    .arg-label { color: var(--colorNeutralForeground3); font-family: var(--fontFamilyBase); font-size: var(--fontSizeBase100); padding-top: 3px; white-space: nowrap; }
    .caret { display: inline-block; width: 2px; height: 17px; margin: 0 -1px; background: var(--colorBrandBackground); animation: blink 1.05s steps(2, start) infinite; vertical-align: text-bottom; }
    @keyframes blink { 50% { opacity: 0; } }
    .band-foot { display: flex; gap: var(--spacingHorizontalS); align-items: baseline; padding: 4px var(--spacingHorizontalS) 6px; border-top: var(--strokeWidthThin) solid var(--colorNeutralStroke2); font-size: var(--fontSizeBase100); color: var(--colorNeutralForeground3); }
    .band-foot .problem { color: var(--colorPaletteRedForeground1, var(--colorStatusDangerForeground1)); }
    .band-foot .saying { color: var(--colorNeutralForeground2); }
    .materials { display: flex; flex-direction: column; gap: var(--spacingVerticalS); flex: none; }
    .signs, .typed { display: flex; flex-wrap: wrap; align-items: center; gap: 4px; padding: var(--spacingVerticalXS) var(--spacingHorizontalS); border: var(--strokeWidthThin) solid var(--colorNeutralStroke2); border-radius: var(--borderRadiusMedium); background: var(--colorNeutralBackground2); }
    .signs .label { color: var(--colorNeutralForeground3); font-size: var(--fontSizeBase100); margin-right: 2px; }
    .signs button, .typed .mode { min-width: 28px; height: 26px; padding: 0 6px; border: var(--strokeWidthThin) solid var(--colorNeutralStroke2); border-radius: var(--borderRadiusMedium); background: var(--colorNeutralBackground1); color: var(--colorNeutralForeground1); font-size: var(--fontSizeBase100); cursor: pointer; }
    .signs button:disabled { opacity: 0.45; cursor: default; }
    .signs button:hover:not(:disabled), .typed .mode:hover { background: var(--colorNeutralBackground1Hover); }
    .typed .mode.on { border-color: var(--colorBrandStroke1); background: var(--colorBrandBackground2); color: var(--colorBrandForeground2); }
    .typed input { flex: 1; min-width: 160px; height: 28px; padding: 0 var(--spacingHorizontalS); border: var(--strokeWidthThin) solid var(--colorNeutralStroke1); border-radius: var(--borderRadiusMedium); background: var(--colorNeutralBackground1); color: var(--colorNeutralForeground1); font-family: var(--fontFamilyMonospace); font-size: var(--fontSizeBase200); }
    .typed .note { color: var(--colorPaletteRedForeground1, var(--colorStatusDangerForeground1)); font-size: var(--fontSizeBase100); }
    .panels { display: flex; gap: var(--spacingHorizontalS); min-height: 160px; flex: 1; }
    .panel { display: flex; flex-direction: column; flex: 1; min-width: 0; padding: var(--spacingVerticalS); background: var(--colorNeutralBackground2); }
    .panel h3 { margin: 0 0 var(--spacingVerticalXS); color: var(--colorNeutralForeground2); font-size: var(--fontSizeBase100); font-weight: var(--fontWeightSemibold); }
    .panel .search { height: 28px; margin-bottom: var(--spacingVerticalXS); padding: 0 var(--spacingHorizontalS); border: var(--strokeWidthThin) solid var(--colorNeutralStroke1); border-radius: var(--borderRadiusMedium); background: var(--colorNeutralBackground1); color: var(--colorNeutralForeground1); font: inherit; font-size: var(--fontSizeBase200); }
    .panel .filters { display: flex; flex-wrap: wrap; gap: 3px; margin-bottom: var(--spacingVerticalXS); }
    .panel .filters button { min-height: 22px; padding: 0 6px; border: var(--strokeWidthThin) solid var(--colorNeutralStroke2); border-radius: var(--borderRadiusSmall); background: var(--colorNeutralBackground1); color: var(--colorNeutralForeground2); font-size: var(--fontSizeBase100); cursor: pointer; }
    .panel .filters button.on { border-color: var(--colorBrandStroke1); background: var(--colorBrandBackground2); color: var(--colorBrandForeground2); }
    .chips { display: flex; flex-wrap: wrap; align-content: flex-start; gap: 4px; flex: 1; min-height: 0; overflow: auto; }
    .chips button { max-width: 100%; min-height: 25px; padding: 2px 7px; border: var(--strokeWidthThin) solid var(--colorNeutralStroke2); border-radius: var(--borderRadiusMedium); background: var(--colorNeutralBackground1); color: var(--colorNeutralForeground1); font-size: var(--fontSizeBase200); overflow: hidden; text-overflow: ellipsis; white-space: nowrap; cursor: pointer; }
    .chips button:hover { border-color: var(--colorBrandStroke2); background: var(--colorBrandBackground2); }
    .chips .empty { color: var(--colorNeutralForeground3); font-size: var(--fontSizeBase200); }
    .chips .new-name { width: 100%; border-style: dashed; color: var(--colorBrandForeground2); }
    .text-body { flex: 1; min-height: 280px; padding: var(--spacingVerticalS); border: var(--strokeWidthThin) solid var(--colorNeutralStroke1); border-radius: var(--borderRadiusMedium); background: var(--colorNeutralBackground2); color: var(--colorNeutralForeground1); font: inherit; font-size: var(--fontSizeBase300); resize: vertical; }
    footer { display: flex; align-items: center; gap: var(--spacingHorizontalS); padding: var(--spacingVerticalS) var(--spacingHorizontalL); border-top: var(--strokeWidthThin) solid var(--colorNeutralStroke2); background: var(--colorNeutralBackground2); }
    footer .hint { flex: 1; color: var(--colorNeutralForeground3); font-size: var(--fontSizeBase100); overflow: hidden; text-overflow: ellipsis; }
    footer .error { color: var(--colorPaletteRedForeground1, var(--colorStatusDangerForeground1)); font-size: var(--fontSizeBase100); }
    footer button { white-space: nowrap; }
    .tone-maths, .tone-numbers { border-color: var(--colorPaletteBlueBorder2, var(--colorNeutralStroke2)); }
    .tone-dates { border-color: var(--colorPaletteGreenBorder2, var(--colorNeutralStroke2)); }
    .tone-records { border-color: var(--colorPalettePurpleBorder2, var(--colorNeutralStroke2)); }
    .tone-text { border-color: var(--colorPaletteGoldBorder2, var(--colorNeutralStroke2)); }
    .tone-lists { border-color: var(--colorPaletteTealBorder2, var(--colorNeutralStroke2)); }
    .tone-logic { border-color: var(--colorPaletteRedBorder2, var(--colorNeutralStroke2)); }
  `;
