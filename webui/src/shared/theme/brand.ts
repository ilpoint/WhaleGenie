import type { BrandVariants } from '@fluentui/tokens';

/**
 * 一个强调色到 Fluent 品牌色阶（10–160 共十六档，80 为主色）的派生。Fluent 的
 * `createDarkTheme/createLightTheme` 吃的是整套色阶，而程序里用户挑的只有一个颜色，所以
 * 这一层就是那道桥：色相与饱和度沿用挑选的颜色，明度沿档位展开，80 档钉在原色上。
 *
 * 派生的对比度底线由 Fluent 的主题派生自己负责；真正的拒绝（颜色根本不可读）仍发生在
 * 程序侧的 `ThemePalette`，这里对不可解析的颜色返回 null，由调用方退回内置主题。
 */

const brandValues = [10, 20, 30, 40, 50, 60, 70, 80, 90, 100, 110, 120, 130, 140, 150, 160] as const;

export function brandFromHex(value: string | null): BrandVariants | null {
  const hsl = parseHsl(value);
  if (hsl === null) {
    return null;
  }

  const [hue, saturation, lightness] = hsl;
  const ramp = {} as Record<(typeof brandValues)[number], string>;
  for (const step of brandValues) {
    ramp[step] = hslToHex(hue, saturation, shadeLightness(step, lightness));
  }
  return ramp;
}

/** 档位的明度：80 以下从暗处收到原色，80 以上从原色亮处散开。 */
function shadeLightness(step: number, base: number): number {
  if (step <= 80) {
    const from = 0.16;
    return from + (base - from) * ((step - 10) / 70);
  }
  const to = 0.97;
  return base + (to - base) * ((step - 80) / 80);
}

/** `#RGB` 与 `#RRGGBB`，其余一律不认。返回 [色相 0-360, 饱和 0-1, 明度 0-1]。 */
function parseHsl(value: string | null): [number, number, number] | null {
  if (!value) {
    return null;
  }

  let digits: string;
  if (/^#[0-9a-fA-F]{6}$/.test(value)) {
    digits = value.slice(1);
  } else if (/^#[0-9a-fA-F]{3}$/.test(value)) {
    digits = value[1]! + value[1]! + value[2]! + value[2]! + value[3]! + value[3]!;
  } else {
    return null;
  }

  const red = parseInt(digits.slice(0, 2), 16) / 255;
  const green = parseInt(digits.slice(2, 4), 16) / 255;
  const blue = parseInt(digits.slice(4, 6), 16) / 255;

  const max = Math.max(red, green, blue);
  const min = Math.min(red, green, blue);
  const lightness = (max + min) / 2;
  if (max === min) {
    return [0, 0, lightness];
  }

  const delta = max - min;
  const saturation = delta / (1 - Math.abs(2 * lightness - 1));
  let hue: number;
  if (max === red) {
    hue = ((green - blue) / delta) % 6;
  } else if (max === green) {
    hue = (blue - red) / delta + 2;
  } else {
    hue = (red - green) / delta + 4;
  }
  return [(hue * 60 + 360) % 360, saturation, lightness];
}

function hslToHex(hue: number, saturation: number, lightness: number): string {
  const c = (1 - Math.abs(2 * lightness - 1)) * saturation;
  const x = c * (1 - Math.abs(((hue / 60) % 2) - 1));
  const m = lightness - c / 2;
  const [r, g, b] =
    hue < 60 ? [c, x, 0]
    : hue < 120 ? [x, c, 0]
    : hue < 180 ? [0, c, x]
    : hue < 240 ? [0, x, c]
    : hue < 300 ? [x, 0, c]
    : [c, 0, x];

  const channel = (value: number): string => Math.round((value + m) * 255).toString(16).padStart(2, '0');
  return `#${channel(r!)}${channel(g!)}${channel(b!)}`;
}
