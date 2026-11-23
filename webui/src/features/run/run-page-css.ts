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

    /* 面板里区块的多少随一次运行变化（有触发行才有那一块），所以不按固定行数排，整块可滚。 */
    .panel {
      display: grid;
      grid-auto-rows: min-content;
      align-content: start;
      gap: var(--spacingVerticalM);
      min-width: 0;
      min-height: 0;
      overflow-y: auto;
    }

    /* 步骤与日志跟着运行增长，各自滚自己的那一块，底下的取景与变量不必等它全部画完。 */
    .steps, .log {
      max-height: 300px;
      overflow-y: auto;
    }

    section > h3 {
      margin: 0 0 var(--spacingVerticalXS);
      font-size: var(--fontSizeBase300);
    }

    .hint {
      margin: 0 0 var(--spacingVerticalXS);
      color: var(--colorNeutralForeground3);
      font-size: var(--fontSizeBase200);
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

    .swap {
      margin: 0;
      padding: var(--spacingVerticalS) var(--spacingHorizontalM);
      border-left: var(--strokeWidthThick) solid var(--colorBrandStroke1);
      border-radius: var(--borderRadiusMedium);
      background: var(--colorNeutralBackground2);
      color: var(--colorNeutralForeground2);
      font-size: var(--fontSizeBase200);
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

    /* 一步的结果与结局贴在这一行里：结局说它怎么结束的，结果说它算出了什么。 */
    .step .outcome {
      font-size: var(--fontSizeBase200);
      color: var(--colorPaletteGreenForeground1);
    }

    .step .result {
      font-size: var(--fontSizeBase200);
      color: var(--colorNeutralForeground2);
      overflow-wrap: anywhere;
    }

    .rule {
      display: grid;
      grid-template-columns: 72px minmax(0, 1fr);
      gap: var(--spacingHorizontalS);
      margin-top: var(--spacingVerticalXS);
    }

    .choices {
      display: flex;
      flex-wrap: wrap;
      gap: var(--spacingHorizontalXS);
      margin-top: var(--spacingVerticalXS);
    }

    .choice {
      padding: 2px var(--spacingHorizontalXS);
      border: var(--strokeWidthThin) solid var(--colorNeutralStroke2);
      border-radius: var(--borderRadiusSmall);
      background: var(--colorNeutralBackground1);
      color: var(--colorNeutralForeground2);
      font-size: var(--fontSizeBase100);
      cursor: pointer;
    }

    .choice:hover {
      background: var(--colorNeutralBackground1Hover);
    }

    .step .detail {
      color: var(--colorNeutralForeground3);
      font-size: var(--fontSizeBase200);
      overflow-wrap: anywhere;
    }

    .trigger-row {
      display: grid;
      grid-template-columns: minmax(110px, 1fr) minmax(0, 1.6fr) auto auto;
      gap: var(--spacingVerticalXS) var(--spacingHorizontalS);
      font-size: var(--fontSizeBase200);
    }

    .trigger-row .status, .trigger-row .checked, .trigger-row .detail {
      color: var(--colorNeutralForeground3);
    }

    .log-line {
      display: grid;
      grid-template-columns: 72px 64px minmax(0, 1fr);
      gap: var(--spacingHorizontalS);
      font-size: var(--fontSizeBase200);
    }

    .log-line .time, .log-line .level {
      color: var(--colorNeutralForeground3);
    }

    .log-line .text {
      overflow-wrap: anywhere;
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

    /* 历史取景：一张缩略图一张卡片，正在看的那张描品牌色，点一下就换过去。 */
    .look-thumbs {
      display: flex;
      flex-wrap: wrap;
      gap: var(--spacingHorizontalS);
      margin-top: var(--spacingVerticalXS);
    }

    .look-thumb {
      display: grid;
      gap: var(--spacingVerticalXS);
      padding: 2px;
      border: var(--strokeWidthThin) solid var(--colorNeutralStroke2);
      border-radius: var(--borderRadiusMedium);
      background: var(--colorNeutralBackground1);
      color: var(--colorNeutralForeground3);
      font-size: var(--fontSizeBase100);
      cursor: pointer;
    }

    .look-thumb.active {
      border-color: var(--colorBrandStroke1);
      color: var(--colorNeutralForeground2);
    }

    .look-thumb:hover {
      background: var(--colorNeutralBackground1Hover);
    }

    .look-thumb img {
      width: 108px;
      height: 60px;
      object-fit: cover;
      border-radius: var(--borderRadiusSmall);
    }

    .rate {
      display: flex;
      flex-wrap: wrap;
      gap: var(--spacingHorizontalS);
      color: var(--colorNeutralForeground3);
      font-size: var(--fontSizeBase200);
    }

    .rate .pair {
      color: var(--colorNeutralForeground2);
    }

    .watch-grid {
      display: grid;
      grid-template-columns: minmax(120px, 1fr) 110px minmax(0, 1.4fr);
      gap: var(--spacingVerticalXS) var(--spacingHorizontalS);
      font-size: var(--fontSizeBase200);
      margin-bottom: var(--spacingVerticalS);
    }

    .watch {
      display: contents;
    }

    .watch span:last-child {
      overflow-wrap: anywhere;
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
