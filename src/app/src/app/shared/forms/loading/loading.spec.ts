import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Loading } from './loading';

describe('Loading', () => {
  let fixture: ComponentFixture<Loading>;

  function compiled(): HTMLElement {
    return fixture.nativeElement as HTMLElement;
  }

  beforeEach(async () => {
    TestBed.configureTestingModule({ imports: [Loading] });
    fixture = TestBed.createComponent(Loading);
    await fixture.whenStable();
    fixture.detectChanges();
  });

  it('says the one word every one of the twenty-two copies said', () => {
    expect(compiled().textContent?.trim()).toBe('Wczytywanie…');
  });

  /** A pause in the reading, not an interruption of it. */
  it('announces itself as a status', () => {
    expect(compiled().querySelector('[role="status"]')?.textContent?.trim()).toBe('Wczytywanie…');
  });

  /**
   * THE REASON THIS EXISTS. A [message] input with a default would be used, and three screens later
   * there would be four sentences for one state. There is no way to reach the word from outside.
   */
  it('offers the caller no way to change the word', () => {
    // Angular attaches __ngContext__ to every instance; anything else here would be a surface a
    // caller could reach.
    const own = Object.keys(fixture.componentInstance).filter((key) => !key.startsWith('__'));

    expect(own).toEqual([]);
  });
});
