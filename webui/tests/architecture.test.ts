import { existsSync, readdirSync, readFileSync, statSync } from 'node:fs';
import { dirname, join, relative, resolve } from 'node:path';
import { describe, expect, it } from 'vitest';

/**
 * The web shell's layering is a source-level contract, not a runtime one. Native
 * no-restricted-imports only sees the specifier string and cannot tell that ../../settings/x is
 * a sibling feature, so the resolved paths are checked here. An allowlist records debt that a
 * later commit removes; it must only shrink.
 */
const sourceRoot = resolve(process.cwd(), 'src').replaceAll('\\', '/');

function walk(dir: string): string[] {
  const found: string[] = [];
  for (const entry of readdirSync(dir, { withFileTypes: true })) {
    const path = join(dir, entry.name);
    if (entry.isDirectory()) {
      found.push(...walk(path));
    } else if (path.endsWith('.ts')) {
      found.push(path);
    }
  }

  return found;
}

const files = walk(sourceRoot);
const relativeToSource = (path: string): string => relative(sourceRoot, path).replaceAll('\\', '/');

function importsOf(file: string): string[] {
  const text = readFileSync(file, 'utf8');
  const specs = new Set<string>();
  for (const match of text.matchAll(/\bfrom\s+['"]([^'"]+)['"]/g)) {
    specs.add(match[1] as string);
  }

  for (const match of text.matchAll(/\bimport\s+['"]([^'"]+)['"]/g)) {
    specs.add(match[1] as string);
  }

  return [...specs];
}

function resolveImport(file: string, spec: string): string | null {
  if (!spec.startsWith('.')) {
    return null;
  }

  let target = resolve(dirname(file), spec);
  if (target.endsWith('.js')) {
    target = target.slice(0, -3) + '.ts';
  }

  if (!existsSync(target)) {
    return null;
  }

  if (statSync(target).isDirectory()) {
    target = join(target, 'index.ts');
  }

  return existsSync(target) ? target : null;
}

function featureOf(path: string): string | null {
  return path.startsWith('features/') ? path.split('/')[1] ?? null : null;
}

const crossFeatureAllow = new Set<string>();

const featureToAppAllow = new Set<string>();

const hostFiles = new Set(['shared/bridge/transport.ts', 'shared/bridge/pick.ts']);

describe('webui architecture', () => {
  it('shared does not depend on features or app', () => {
    const violations: string[] = [];
    for (const file of files) {
      const from = relativeToSource(file);
      if (!from.startsWith('shared/')) {
        continue;
      }

      for (const spec of importsOf(file)) {
        const resolved = resolveImport(file, spec);
        if (!resolved) {
          continue;
        }

        const to = relativeToSource(resolved);
        if (to.startsWith('features/') || to.startsWith('app/')) {
          violations.push(from + ' -> ' + to);
        }
      }
    }

    expect(violations).toEqual([]);
  });

  it('features do not depend on the app shell', () => {
    const violations: string[] = [];
    for (const file of files) {
      const from = relativeToSource(file);
      if (!from.startsWith('features/')) {
        continue;
      }

      for (const spec of importsOf(file)) {
        const resolved = resolveImport(file, spec);
        if (!resolved) {
          continue;
        }

        const to = relativeToSource(resolved);
        const edge = from + ' -> ' + to;
        if (to.startsWith('app/') && !featureToAppAllow.has(edge)) {
          violations.push(edge);
        }
      }
    }

    expect(violations).toEqual([]);
  });

  it('cross-feature imports go through the other feature index', () => {
    const violations: string[] = [];
    for (const file of files) {
      const from = relativeToSource(file);
      const own = featureOf(from);
      if (!own) {
        continue;
      }

      for (const spec of importsOf(file)) {
        const resolved = resolveImport(file, spec);
        if (!resolved) {
          continue;
        }

        const to = relativeToSource(resolved);
        const other = featureOf(to);
        const edge = from + ' -> ' + to;
        if (other && other !== own && !to.endsWith('/index.ts') && !crossFeatureAllow.has(edge)) {
          violations.push(edge);
        }
      }
    }

    expect(violations).toEqual([]);
  });

  it('only the bridge transport and picker touch the host globals', () => {
    const violations: string[] = [];
    for (const file of files) {
      const path = relativeToSource(file);
      if (hostFiles.has(path)) {
        continue;
      }

      const text = readFileSync(file, 'utf8');
      if (text.includes('invokeCSharpAction') || text.includes('__whale')) {
        violations.push(path);
      }
    }

    expect(violations).toEqual([]);
  });

  it('editor state, services, and models stay free of lit and DOM events', () => {
    const violations: string[] = [];
    for (const file of files) {
      const from = relativeToSource(file);
      if (!/^features\/[^/]+\/(services|state|model)\//.test(from)) {
        continue;
      }

      for (const spec of importsOf(file)) {
        if (spec === 'lit' || spec.startsWith('lit/')) {
          violations.push(from + ' -> ' + spec);
        }
      }
    }

    expect(violations).toEqual([]);
  });
});
