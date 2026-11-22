import eslint from '@eslint/js';
import tseslint from 'typescript-eslint';

export default tseslint.config(
  eslint.configs.recommended,
  ...tseslint.configs.recommended,
  {
    files: ['**/*.ts'],
    rules: {
      '@typescript-eslint/no-explicit-any': 'error',
      '@typescript-eslint/consistent-type-imports': 'error',
      'no-console': ['error', { allow: ['error'] }],
      'prefer-const': 'error',
      eqeqeq: 'error',
    },
  },
  {
    // The web shell is read whole: a file or function that no longer fits one screen is a
    // signal to split by concern, not a hard style preference. Style-only modules are exempt
    // because a stylesheet has one job and no branches.
    files: ['src/**/*.ts'],
    rules: {
      'max-lines': ['error', { max: 400, skipBlankLines: true, skipComments: true }],
      'max-lines-per-function': ['error', { max: 80, skipBlankLines: true, skipComments: true }],
      complexity: ['error', 15],
      'max-depth': ['error', 4],
      'no-restricted-properties': [
        'error',
        {
          object: 'window',
          property: 'invokeCSharpAction',
          message: 'Talk to the host through shared/bridge; the transport is the one adapter.',
        },
        {
          object: 'window',
          property: '__whale',
          message: 'The host receive port belongs to shared/bridge/transport.ts.',
        },
      ],
    },
  },
  {
    files: ['src/**/*-css.ts'],
    rules: {
      'max-lines': 'off',
      'max-lines-per-function': 'off',
    },
  },
  {
    // Test files are scenario scripts: a long Playwright test is a sequence, not a function
    // that needs a new abstraction.
    files: ['tests/**/*.ts', 'e2e/**/*.ts'],
    rules: {
      'max-lines': ['error', { max: 800, skipBlankLines: true, skipComments: true }],
      'max-lines-per-function': 'off',
    },
  },
  {
    files: ['src/shared/bridge/transport.ts', 'src/shared/bridge/pick.ts'],
    rules: {
      'no-restricted-properties': 'off',
    },
  },
  {
    ignores: ['dist/**', 'node_modules/**', 'playwright-report/**', 'test-results/**'],
  },
);
