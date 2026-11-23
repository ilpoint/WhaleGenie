/** 本地图片的路径，页面从 file:// 加载宏图标时用。 */
export function fileUrl(path: string): string {
  const slash = path.replaceAll('\\', '/');
  return slash.startsWith('/') ? 'file://' + encodeURI(slash) : 'file:///' + encodeURI(slash);
}
