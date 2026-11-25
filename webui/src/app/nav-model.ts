/**
 * 把一个组合键拆成显示用的键位。程序写下的形式就是 `Ctrl+K+L`；单键只有一项。
 * "+" 本身在键词汇表里写作 `Plus`，所以按 "+" 拆不会把它拆丢。
 */
export function hotkeyKeys(display: string): string[] {
  return display
    .split('+')
    .map((part) => part.trim())
    .filter((part) => part.length > 0);
}
