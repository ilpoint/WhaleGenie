import { defineConfig, type Plugin, type Rollup } from 'vite';

/**
 * 把构建产物压回一个自包含的 index.html：脚本内联为 <script type="module">，样式内联为
 * <style>。内联的模块脚本在 file:// 下照常执行（外链模块才会被 CORS 拦住），网页里也没有
 * 任何要请求的资源——数据都走桥，字体用系统字体——所以程序只需带这一个文件。
 */
function inlineBundle(): Plugin {
  return {
    name: 'inline-bundle',
    apply: 'build',
    // Vite 的 HTML 插件在核心插件层生成 index.html；post 保证这个钩子跑在它之后。
    enforce: 'post',
    generateBundle(_options, bundle) {
      let html: Rollup.OutputAsset | undefined;
      for (const entry of Object.values(bundle)) {
        if (entry.type === 'asset' && entry.fileName.endsWith('.html')) {
          html = entry;
        }
      }
      if (!html) {
        throw new Error('inline-bundle: the build produced no index.html');
      }

      let source = asText(html.source);
      for (const entry of Object.values(bundle)) {
        if (entry.type === 'chunk' && entry.isEntry) {
          // 内联脚本里出现 </script 会提前闭合标签，转义掉；网页代码不含 script 标签，
          // 这一条只为防御未来的意外。
          const code = entry.code.replace(/<\/script/gi, '<\\/script');
          source = replaceTag(source, '<script type="module" crossorigin', `<script type="module">\n${code}\n</script>`);
          entry.code = '';
          delete bundle[entry.fileName];
        }
      }

      for (const entry of Object.values(bundle)) {
        if (entry.type === 'asset' && entry.fileName.endsWith('.css')) {
          source = replaceTag(source, '<link rel="stylesheet" crossorigin', `<style>\n${asText(entry.source)}\n</style>`);
          delete bundle[entry.fileName];
        }
      }

      html.source = source;
    },
  };
}

function asText(source: string | Uint8Array): string {
  return typeof source === 'string' ? source : new TextDecoder().decode(source);
}

/**
 * Vite 生成的标签属性顺序稳定，按前缀找到整条标签替换即可。找不到就报错：内联失败必须让
 * 构建失败，而不是静默出一份引用不存在文件的页面。
 */
function replaceTag(html: string, prefix: string, replacement: string): string {
  const start = html.indexOf(prefix);
  if (start < 0) {
    throw new Error(`inline-bundle: no tag starting with ${JSON.stringify(prefix)}`);
  }
  const end = html.indexOf('>', start);
  if (end < 0) {
    throw new Error(`inline-bundle: tag starting with ${JSON.stringify(prefix)} is not closed`);
  }
  return html.slice(0, start) + replacement + html.slice(end + 1);
}

export default defineConfig({
  base: './',
  build: {
    cssCodeSplit: false,
    assetsInlineLimit: 100_000_000,
    rollupOptions: {
      output: {
        inlineDynamicImports: true,
      },
    },
  },
  plugins: [inlineBundle()],
});
