import { describe, expect, it } from 'vitest';
import { strings, t } from '../src/shared/strings/strings.js';

describe('strings store', () => {
  it('按键取词，词表没到的键显示键名本身', () => {
    expect(t('Main.Title')).toBe('Main.Title');

    strings.load({ language: 'zh-Hans', entries: { 'Main.Title': '鲸灵' } });
    expect(t('Main.Title')).toBe('鲸灵');
    expect(t('Missing.Key')).toBe('Missing.Key');
    expect(strings.language).toBe('zh-Hans');
  });

  it('换包时通知订阅者，页面因此能重画', () => {
    // 两个用例共用同一份单例：先清空，订阅当下收到的才是空表。
    strings.load({ language: 'zh-Hans', entries: {} });
    const seen: number[] = [];
    // 订阅当下就收到当前值（空表），这是"先订阅再画"的约定。
    const stop = strings.subscribe((bundle) => seen.push(Object.keys(bundle.entries).length));

    strings.load({ language: 'en', entries: { 'Shell.Home': 'Home' } });
    strings.load({ language: 'zh-Hans', entries: { 'Shell.Home': '首页', 'Main.Title': '鲸灵' } });
    stop();
    strings.load({ language: 'en', entries: {} });

    expect(seen).toEqual([0, 1, 2]);
  });
});
