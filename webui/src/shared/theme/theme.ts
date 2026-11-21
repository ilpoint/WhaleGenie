import { setTheme } from '@fluentui/web-components';
import {
  createDarkTheme,
  createLightTheme,
  webDarkTheme,
  webLightTheme,
  type Theme,
} from '@fluentui/tokens';
import { Store } from '../store.js';
import { brandFromHex } from './brand.js';
import type { ThemeState } from '../bridge/protocol.js';

/**
 * 主题：深浅两套走 Fluent 自带的主题令牌，自定义强调色由品牌色阶现场派生一套。程序里
 * `ThemeController` 是唯一权威——它先做对比度底线检查并落盘，这里只把派生结果铺给页面。
 * 页面不读设置文件，也不自己发明颜色。
 */

class ThemeStore {
  private readonly state = new Store<ThemeState>({ mode: 'dark', baseLight: false, accentHex: null });

  subscribe(listener: (theme: ThemeState) => void): () => void {
    return this.state.subscribe(listener);
  }

  /**
   * 仅供启动装配与事件处理：灌入一份主题状态并立刻铺开。铺开不能等订阅者——启动时
   * `theme.get` 的结果到达时页面上还没有人订阅，而令牌是全局的，迟一拍页面就按默认色画了。
   */
  load(theme: ThemeState): void {
    this.state.set(theme);
    this.paint(theme);
  }

  private paint(theme: ThemeState): void {
    // Theme 对象本身就是"键 camelCase、值真实颜色"的形状，setTheme 直接铺它；themeToTokensObject
    // 产出的是 var() 引用层，铺上去只会得到一整页自引用。
    setTheme(tokensFor(theme));
    document.documentElement.dataset.theme = theme.baseLight ? 'light' : 'dark';
  }
}

/** 这一种选择用哪一套令牌：内置两套用 Fluent 默认主题，自定义强调色由色阶派生。 */
function tokensFor(theme: ThemeState): Theme {
  if (theme.mode !== 'custom') {
    return theme.baseLight ? webLightTheme : webDarkTheme;
  }

  // 没给颜色（或给的不是一个颜色）时回到内置主题：派生失败不是页面自己能修的事。
  const brand = brandFromHex(theme.accentHex);
  if (brand === null) {
    return theme.baseLight ? webLightTheme : webDarkTheme;
  }
  return theme.baseLight ? createLightTheme(brand) : createDarkTheme(brand);
}

export const theme = new ThemeStore();
