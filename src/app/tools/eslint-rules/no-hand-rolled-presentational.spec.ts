import { RuleTester } from '@angular-eslint/test-utils';

// require(), on purpose: eslint.config.js is CommonJS and loads the rule that way with no build
// step, so importing it any other way here would test something the linter never runs.
//
// Declared here rather than by adding "node" to tsconfig.spec.json's types: that would hand
// every app spec Node's globals too, in a suite that runs in a browser environment.
// eslint-disable-next-line @typescript-eslint/no-explicit-any
declare function require(id: string): any;

const rule = require('./no-hand-rolled-presentational.js');
const templateParser = require('@angular-eslint/template-parser');

/**
 * THE NEGATIVE CASES ARE THE POINT.
 *
 * This rule is the only code in the project that can block every merge, and a rule that over-fires
 * is worse than no rule: it teaches the next contributor to reach for a disable comment. The
 * `valid` block below is therefore not a formality — each entry is a shape that exists in the tree
 * today and must keep passing.
 */
const ruleTester = new RuleTester({
  languageOptions: { parser: templateParser },
});

ruleTester.run('no-hand-rolled-presentational', rule, {
  valid: [
    // The kit, used as intended.
    `<app-field label="Adres e-mail" for="email"><input id="email" /></app-field>`,
    `<app-select><select id="a" name="a"></select></app-select>`,
    `<app-loading />`,
    `<app-empty>Nie masz jeszcze żadnych zapisów.</app-empty>`,
    `<app-checkbox [checked]="on()" (checkedChange)="toggle()">Pokaż nieaktywne</app-checkbox>`,
    `<app-list><li appRow class="card"><p class="row-name">Anna</p></li></app-list>`,

    // A select nested deeper inside the wrapper, which the bookings overlay does: the check is for
    // an ANCESTOR, not for a parent.
    `<app-select><div class="bookings-add-controls"><select id="a"></select></div></app-select>`,

    // plan-builder.html:45 — the literal string <select> inside a COMMENT, explaining why that
    // branch renders static text. A scan over the source text flags this; the parser never sees it.
    `<!-- STATIC TEXT, not a disabled <select>. The picker carries only ACTIVE accounts. -->`,

    // Substring traps. "field" and "empty" are whole class tokens, not fragments of other names.
    `<fieldset class="exercise-fieldset"><legend>Ćwiczenie</legend></fieldset>`,
    `<div class="battlefield-notes"></div>`,
    `<div class="row-identity"></div>`,

    // A real per-screen difference on an element that IS a row: the bookings list's border, and
    // the passes list's current-pass modifier. Neither is a copy of the recipe.
    `<li appRow class="bookings-row"><p class="row-name">Anna</p></li>`,
    `<li appRow class="card" [class.passes-row--current]="p.coversToday"></li>`,

    // An app-field labelled by binding rather than by a static attribute.
    `<app-field [label]="caption()" for="a"><input id="a" /></app-field>`,

    // plan-builder's shape: no label input, and the slotted label sits inside an @if on both
    // branches. The check has to look through the control-flow block to find it.
    `<app-field>
      @if (editing()) {
        <span slot="label" class="builder-caption">Członek</span>
      } @else {
        <label slot="label" for="plan-member">Członek</label>
      }
      <input id="plan-member" />
    </app-field>`,

    // Not a checkbox.
    `<input type="text" id="name" />`,
    `<input type="number" id="capacity" />`,
  ],

  invalid: [
    {
      code: `<select id="classTypeId" name="classTypeId"></select>`,
      errors: [{ messageId: 'useAppSelect' }],
    },
    {
      code: `<div class="field"><label for="a">A</label><input id="a" /></div>`,
      errors: [{ messageId: 'useAppField' }],
    },
    {
      code: `<label class="field" for="a">A</label>`,
      errors: [{ messageId: 'useAppField' }],
    },
    {
      code: `<p class="empty">Plan jest pusty.</p>`,
      errors: [{ messageId: 'useAppEmpty' }],
    },
    {
      code: `<input type="checkbox" [checked]="on()" />`,
      errors: [{ messageId: 'useAppCheckbox' }],
    },
    {
      code: `<p class="notice" role="status">Wczytywanie…</p>`,
      errors: [{ messageId: 'useAppLoading' }],
    },
    {
      code: `<li class="card plans-row"><p>Anna</p></li>`,
      errors: [{ messageId: 'useAppRow' }],
    },
    {
      // An app-field that names nothing: projection cannot catch this, so the rule has to.
      code: `<app-field for="email"><input id="email" /></app-field>`,
      errors: [{ messageId: 'labelAppField' }],
    },
    {
      // A slotted label belonging to a NESTED field does not label the outer one.
      code: `<app-field><app-field><span slot="label">A</span></app-field></app-field>`,
      errors: [{ messageId: 'labelAppField' }],
    },
    {
      // The wrapper's own class, hand-written instead of app-select.
      code: `<span class="select"><select id="a"></select></span>`,
      errors: [{ messageId: 'useAppSelect' }, { messageId: 'useAppSelect' }],
    },
  ],
});
