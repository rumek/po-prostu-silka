// @ts-check
const eslint = require('@eslint/js');
const { defineConfig } = require('eslint/config');
const tseslint = require('typescript-eslint');
const angular = require('angular-eslint');

module.exports = defineConfig([
  {
    files: ['**/*.ts'],
    extends: [
      eslint.configs.recommended,
      tseslint.configs.recommended,
      tseslint.configs.stylistic,
      angular.configs.tsRecommended,
    ],
    processor: angular.processInlineTemplates,
    rules: {
      '@angular-eslint/directive-selector': [
        'error',
        {
          type: 'attribute',
          prefix: 'app',
          style: 'camelCase',
        },
      ],
      // Two entries, not one, since S-23. Components are elements here as they always were; the
      // exception is `li[appRow]`, which HAS to be an attribute: as an element it would make the
      // DOM `ul > app-row`, and a <ul> admits nothing but <li>. The attribute spelling follows the
      // camelCase this file already requires of directive selectors.
      '@angular-eslint/component-selector': [
        'error',
        [
          {
            type: 'element',
            prefix: 'app',
            style: 'kebab-case',
          },
          {
            type: 'attribute',
            prefix: 'app',
            style: 'camelCase',
          },
        ],
      ],
    },
  },
  {
    files: ['**/*.html'],
    extends: [angular.configs.templateRecommended, angular.configs.templateAccessibility],
    rules: {},
  },
  // The rule itself (S-23). Plain CommonJS with no build step, so it gets JS recommended rules and
  // the two CommonJS globals it uses — otherwise listing tools/**/*.js in lintFilePatterns would
  // lint it against an empty rule set and prove nothing.
  {
    files: ['tools/**/*.js'],
    extends: [eslint.configs.recommended],
    languageOptions: {
      sourceType: 'commonjs',
      globals: { require: 'readonly', module: 'writable' },
    },
  },
  // -----------------------------------------------------------------------------
  // The presentational kit's enforcement (S-23).
  //
  // Scoped to feature templates, and the boundary is structural rather than a list of exemptions
  // to maintain: the kit lives in `shared/`, screens live in `features/`. `app-select`'s own
  // template contains the <select> the rule forbids, and would fail its own rule under any wider
  // scope.
  //
  // The cost is real and recorded in AGENTS.md: `shared/` was migrated in S-23 too, and nothing
  // stops it drifting back.
  // -----------------------------------------------------------------------------
  {
    files: ['src/app/features/**/*.html'],
    plugins: {
      'po-prostu-silka': {
        rules: {
          'no-hand-rolled-presentational': require('./tools/eslint-rules/no-hand-rolled-presentational.js'),
        },
      },
    },
    rules: {
      'po-prostu-silka/no-hand-rolled-presentational': 'error',
    },
  },
]);
