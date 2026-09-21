import { DESK_MEDIA_QUERY, DESK_MIN_WIDTH } from './breakpoints';

// require(), and declared here rather than by adding "node" to tsconfig.spec.json's types: that
// would hand every app spec Node's globals too, in a suite that runs in a browser environment. Same
// precedent as tools/eslint-rules/no-hand-rolled-presentational.spec.ts.
// eslint-disable-next-line @typescript-eslint/no-explicit-any
declare function require(id: string): any;
// eslint-disable-next-line @typescript-eslint/no-explicit-any
declare const process: any;

const fs = require('node:fs');
const path = require('node:path');

/** `npm test` runs from `src/app/`, the Angular workspace root. */
const SRC = path.join(process.cwd(), 'src');
const PARTIAL = path.join(SRC, 'styles', '_breakpoints.scss');

/** A width media condition written as a number — the thing only the partial may contain. */
const LITERAL_WIDTH_MEDIA = /@media[^{]*(min|max)-width\s*:\s*[0-9]/;

function scssFiles(dir: string): string[] {
  return fs
    .readdirSync(dir, { withFileTypes: true })
    .flatMap((entry: { name: string; isDirectory(): boolean }) => {
      const full = path.join(dir, entry.name);

      if (entry.isDirectory()) {
        return scssFiles(full);
      }

      return entry.name.endsWith('.scss') ? [full] : [];
    });
}

/**
 * "One definition of a breakpoint" (S-20, UX-04), enforced rather than asserted in a comment — the
 * same move S-23 made for the presentational kit. Two ways it could quietly stop being true, one
 * test each.
 */
describe('breakpoints', () => {
  it('keeps the Sass and TypeScript desk boundaries equal', () => {
    const partial: string = fs.readFileSync(PARTIAL, 'utf8');
    const declared = /^\$desk-min-width:\s*([^;]+);/m.exec(partial)?.[1].trim();

    expect(declared).toBe(DESK_MIN_WIDTH);
    expect(DESK_MEDIA_QUERY).toBe(`(min-width: ${DESK_MIN_WIDTH})`);
  });

  it('writes no width media query outside the partial', () => {
    const offenders = scssFiles(SRC)
      .filter((file) => path.resolve(file) !== path.resolve(PARTIAL))
      .filter((file) => LITERAL_WIDTH_MEDIA.test(fs.readFileSync(file, 'utf8')))
      .map((file) => path.relative(SRC, file));

    expect(offenders).toEqual([]);
  });

  it('scans a tree that actually has stylesheets', () => {
    // Guards the test above against passing vacuously on a wrong working directory.
    expect(scssFiles(SRC).length).toBeGreaterThan(10);
  });
});
