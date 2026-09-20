import { Component, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { List } from './list';
import { Row } from './row';

@Component({
  imports: [List, Row],
  template: `
    <app-list>
      <li appRow class="card">
        <img slot="lead" src="thumb.png" alt="" />
        <p class="row-name">Przysiad ze sztangą</p>
        <p class="row-meta">Nogi</p>
        <div slot="actions"><button type="button">Otwórz</button></div>
        <p slot="foot" class="field-error" role="alert">Nie udało się.</p>
      </li>
      <li appRow class="card">
        <p class="row-name">Martwy ciąg</p>
        @if (failed()) {
          <p slot="foot" class="field-error" role="alert">Nie udało się.</p>
        }
      </li>
    </app-list>
  `,
})
class Host {
  readonly failed = signal(true);
}

describe('List and Row', () => {
  let fixture: ComponentFixture<Host>;

  function compiled(): HTMLElement {
    return fixture.nativeElement as HTMLElement;
  }

  beforeEach(async () => {
    TestBed.configureTestingModule({ imports: [Host] });
    fixture = TestBed.createComponent(Host);
    await fixture.whenStable();
    fixture.detectChanges();
  });

  /**
   * THE REASON Row IS AN ATTRIBUTE SELECTOR. As an element it would produce `ul > app-row`, and a
   * <ul> admits nothing but <li> — a list that reports no length to anyone reading it aloud.
   */
  it('produces real list markup, with every row a direct <li> child of the <ul>', () => {
    const list = compiled().querySelector('ul.list');

    expect(list).not.toBeNull();
    expect(list?.children).toHaveLength(2);
    for (const child of Array.from(list!.children)) {
      expect(child.tagName).toBe('LI');
      expect(child.classList.contains('row')).toBe(true);
    }
  });

  it('projects all four slots, in the order a row reads in', () => {
    const row = compiled().querySelector('li.row') as HTMLElement;
    const parts = Array.from(row.children).map((el) => el.tagName + '.' + (el.className || '-'));

    expect(parts).toEqual(['IMG.-', 'DIV.row-identity', 'DIV.-', 'P.field-error']);
  });

  it('puts the default content in the identity block', () => {
    const identity = compiled().querySelector('li.row > .row-identity');

    expect(identity?.querySelector('.row-name')?.textContent?.trim()).toBe('Przysiad ze sztangą');
    expect(identity?.querySelector('.row-meta')?.textContent?.trim()).toBe('Nogi');
  });

  /** A row with nothing but a name is still a row — my-classes and plans have no lead or actions. */
  it('renders nothing at all for a slot the row did not fill', () => {
    const bare = compiled().querySelectorAll('li.row')[1];

    expect(bare.querySelector('img')).toBeNull();
    expect(bare.querySelector('[slot=actions]')).toBeNull();
    expect(bare.querySelector('.row-name')?.textContent?.trim()).toBe('Martwy ciąg');
  });

  /**
   * THE ONE THING THAT HAD TO BE MEASURED RATHER THAN ASSUMED. Every per-row failure line in the
   * app sits inside an @if, and a control-flow block is not a static element a projection selector
   * can match — so whether `slot="foot"` survives the block decides whether those lines land
   * beside the name or under the row. Six screens depend on the answer.
   */
  it('honours a slot on content inside an @if', () => {
    const bare = compiled().querySelectorAll('li.row')[1];
    const foot = bare.querySelector('.field-error');

    expect(foot).not.toBeNull();
    expect(foot?.parentElement?.classList.contains('row-identity')).toBe(false);
    expect(foot?.parentElement).toBe(bare);
  });

  /** `card` is the caller's: the bookings list sits inside a panel that is already one. */
  it('leaves the card class to the caller', () => {
    expect(Row.prototype).toBeDefined();
    expect(compiled().querySelector('li.row.card')).not.toBeNull();
  });
});
