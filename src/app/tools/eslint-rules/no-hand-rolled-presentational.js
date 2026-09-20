// @ts-check
const { getTemplateParserServices } = require('@angular-eslint/utils');

/**
 * THE ENFORCEMENT HALF OF S-23's DECISION, and the reason the kit is a kit rather than a ninth way
 * to draw a field.
 *
 * Every component in the kit PROJECTS rather than owns — that is what let `app-field` absorb all 56
 * copies including plan-builder's branching label and profile's server/fallback pair. The price of
 * projection is that a component cannot check what was projected into it, or notice that a screen
 * hand-rolled the block instead of calling it at all. This rule is what pays that price.
 *
 * WHY AN AST RULE AND NOT A SPEC THAT GREPS THE TEMPLATES. Two checks here cannot be done on text:
 *
 *   - a `<select>` is legal exactly when it has an `app-select` ANCESTOR, which is a tree question;
 *   - `plan-builder.html` contains the literal string `<select>` inside an HTML COMMENT explaining
 *     why that branch renders static text instead. A regex scan flags it. The parser does not see
 *     it at all.
 *
 * Scoped to `app/features/**` by `eslint.config.js`. The boundary is structural rather than a list
 * of exemptions to maintain: the kit lives in `shared/`, screens live in `features/`. The cost is
 * recorded in AGENTS.md — `shared/` was migrated too, and nothing stops it drifting back.
 */

/** Class names a screen must no longer write by hand, and what to write instead. */
const BANNED_CLASSES = {
  field: 'useAppField',
  empty: 'useAppEmpty',
  select: 'useAppSelect',
};

/** The one word every loading state in the app says. It lives in app-loading, not in a template. */
const LOADING_TEXT = /Wczytywanie/;

/** Reads a static class attribute off a template element. Bound [class] is not our business. */
function classTokens(node) {
  const attribute = (node.attributes || []).find((candidate) => candidate.name === 'class');
  if (!attribute || typeof attribute.value !== 'string') {
    return [];
  }
  return attribute.value.split(/\s+/).filter(Boolean);
}

function hasAncestor(node, predicate) {
  for (let current = node.parent; current; current = current.parent) {
    if (predicate(current)) {
      return true;
    }
  }
  return false;
}

/** @type {import('eslint').Rule.RuleModule} */
module.exports = {
  meta: {
    type: 'problem',
    docs: {
      description:
        'Screens use the presentational kit in shared/forms and shared/list rather than ' +
        'hand-rolling one of the six blocks it exists to hold (S-23).',
    },
    schema: [],
    messages: {
      useAppSelect:
        'Wrap this <select> in <app-select>. Outside that wrapper it has no arrow at all: ' +
        'styles.scss removes the platform chevron and draws the replacement from .select::after, ' +
        'which a <select> cannot generate for itself. See AGENTS.md, "The presentational kit".',
      useAppField:
        'Use <app-field label="…" for="…"> instead of class="field". ' +
        'See AGENTS.md, "The presentational kit".',
      useAppEmpty:
        'Use <app-empty> instead of class="empty". See AGENTS.md, "The presentational kit".',
      useAppCheckbox:
        'Use <app-checkbox> instead of a bare <input type="checkbox">, so the box is styled and ' +
        'its label cannot come unattached. See AGENTS.md, "The presentational kit".',
      useAppLoading:
        'Use <app-loading /> instead of writing "Wczytywanie…". The word lives in the component ' +
        'so that the app cannot end up with two of it. See AGENTS.md, "The presentational kit".',
      useAppRow:
        'Use <li appRow> and the shared .row-* classes instead of a per-screen "{{ name }}" ' +
        'class. See AGENTS.md, "The presentational kit".',
    },
  },

  create(context) {
    const parserServices = getTemplateParserServices(context);

    function report(node, messageId, data) {
      context.report({
        loc: parserServices.convertNodeSourceSpanToLoc(node.sourceSpan),
        messageId,
        data,
      });
    }

    return {
      'Element$1, Element'(node) {
        const name = node.name;

        // A <select> is fine — inside app-select, which is the only thing that gives it an arrow.
        if (name === 'select') {
          const wrapped = hasAncestor(node, (ancestor) => ancestor.name === 'app-select');
          if (!wrapped) {
            report(node, 'useAppSelect');
          }
        }

        if (name === 'input') {
          const type = (node.attributes || []).find((attribute) => attribute.name === 'type');
          if (type && type.value === 'checkbox') {
            report(node, 'useAppCheckbox');
          }
        }

        for (const token of classTokens(node)) {
          const messageId = BANNED_CLASSES[token];
          if (messageId) {
            report(node, messageId);
            continue;
          }

          // A per-screen copy of the row recipe. `bookings-row` and `passes-row--current` are real
          // per-screen differences rather than copies, and sit on an element that already carries
          // the appRow attribute — which is what tells the two apart.
          const isRowName = /-row$/.test(token);
          const alreadyARow = (node.attributes || []).some(
            (attribute) => attribute.name === 'appRow',
          );
          const boundRow = (node.inputs || []).some((input) => input.name === 'appRow');
          if (isRowName && !alreadyARow && !boundRow) {
            report(node, 'useAppRow', { name: token });
          }
        }
      },

      'Text$3, Text'(node) {
        if (typeof node.value === 'string' && LOADING_TEXT.test(node.value)) {
          report(node, 'useAppLoading');
        }
      },
    };
  },
};
