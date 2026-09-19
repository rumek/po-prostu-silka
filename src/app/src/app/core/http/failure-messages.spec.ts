import { createFailureMessages } from './failure-messages';
import { transportMessage } from './transport-messages';

describe('createFailureMessages', () => {
  const message = createFailureMessages(
    { too_late: 'Za późno.', taken: 'Zajęte.' },
    'Nie udało się. Spróbuj ponownie.',
  );

  it('answers a known reason with its sentence', () => {
    expect(message('too_late')).toBe('Za późno.');
  });

  it('answers an unmapped reason with the fallback', () => {
    // A server one version ahead can name a reason this build has never heard of.
    expect(message('brand_new_reason')).toBe('Nie udało się. Spróbuj ponownie.');
  });

  it('answers a non-string with the fallback rather than undefined', () => {
    expect(message(undefined)).toBe('Nie udało się. Spróbuj ponownie.');
    expect(message(null)).toBe('Nie udało się. Spróbuj ponownie.');
    expect(message(42)).toBe('Nie udało się. Spróbuj ponownie.');
  });

  /**
   * THE `Object.hasOwn` GUARD, pinned. With `in` instead, a server reason of "constructor"
   * would resolve up the prototype chain to a FUNCTION typed as string and render as source
   * text in front of a user. This is a review finding, not a style preference —
   * context/archive/2026-09-02-schedule-calendar-view/reviews/impl-review.md:169-174.
   */
  it.each(['constructor', 'toString', 'hasOwnProperty', '__proto__'])(
    'answers the inherited property %s with the fallback string',
    (reason) => {
      const answer = message(reason);

      expect(typeof answer).toBe('string');
      expect(answer).toBe('Nie udało się. Spróbuj ponownie.');
    },
  );
});

describe('transportMessage', () => {
  it('returns null for a business failure so the union table answers instead', () => {
    expect(transportMessage({ kind: 'business', reason: 'class_full', status: 409 })).toBeNull();
  });

  it('gives each non-business kind its own sentence', () => {
    const kinds = ['auth', 'notFound', 'rateLimited', 'server', 'offline', 'unknown'] as const;
    const sentences = kinds.map((kind) => transportMessage({ kind }));

    for (const sentence of sentences) {
      expect(sentence).toBeTruthy();
    }

    // The point of the table is that a 429 does not read like a 500 or like a dead network.
    expect(new Set(sentences).size).toBe(kinds.length);
  });

  it('names the network specifically when offline', () => {
    expect(transportMessage({ kind: 'offline' })).toContain('połączenia');
  });
});
