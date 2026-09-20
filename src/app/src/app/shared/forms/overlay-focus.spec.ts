import { Component, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { useOverlayFocus } from './overlay-focus';

/**
 * A stand-in for the three real overlays, carrying only what the helper actually looks for: a
 * `.overlay-panel` inside the component's own host element. Testing against this rather than
 * against `class-create-overlay` keeps the helper's contract separable from any one screen's
 * inputs — and the three consumers each pin their own Escape-to-close behaviour already.
 */
@Component({
  selector: 'app-test-overlay',
  template: `
    <div class="overlay-backdrop"></div>
    <div class="overlay-panel">
      <h2 class="overlay-title">Tytul</h2>
      <button type="button">W srodku</button>
    </div>
  `,
})
class TestOverlay {
  // Called from a field initialiser, exactly as the three real overlays call it — it has to run
  // early enough to read the active element before the panel takes it.
  private readonly focus = useOverlayFocus();
}

/** A panel-less overlay: the helper must not throw when its selector finds nothing. */
@Component({
  selector: 'app-test-overlay-empty',
  template: `<div class="not-a-panel"></div>`,
})
class TestOverlayWithoutPanel {
  private readonly focus = useOverlayFocus();
}

@Component({
  imports: [TestOverlay, TestOverlayWithoutPanel],
  template: `
    <button type="button" id="opener">Otworz</button>
    @if (open()) {
      <app-test-overlay />
    }
    @if (openEmpty()) {
      <app-test-overlay-empty />
    }
  `,
})
class Host {
  readonly open = signal(false);
  readonly openEmpty = signal(false);
}

describe('useOverlayFocus', () => {
  let fixture: ComponentFixture<Host>;
  let host: Host;
  let opener: HTMLButtonElement;

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [Host] }).compileComponents();

    fixture = TestBed.createComponent(Host);
    host = fixture.componentInstance;
    fixture.detectChanges();
    await fixture.whenStable();

    opener = fixture.nativeElement.querySelector('#opener');
    // The overlay is opened by a click in the real app, so the opener IS the active element at the
    // moment the component is constructed. Focusing it here is what makes that true in the test.
    opener.focus();
    expect(document.activeElement).toBe(opener);
  });

  const openOverlay = async (): Promise<void> => {
    host.open.set(true);
    fixture.detectChanges();
    await fixture.whenStable();
  };

  it('moves focus into the panel on open', async () => {
    await openOverlay();

    const panel = fixture.nativeElement.querySelector('.overlay-panel');

    // The PANEL, not the button inside it. Focusing the first control would skip the title, and a
    // screen-reader user would never hear what the dialog is about.
    expect(document.activeElement).toBe(panel);
    expect(panel.tabIndex).toBe(-1);
  });

  it('returns focus to the opener on close', async () => {
    await openOverlay();
    expect(document.activeElement).not.toBe(opener);

    host.open.set(false);
    fixture.detectChanges();
    await fixture.whenStable();

    expect(document.activeElement).toBe(opener);
  });

  /**
   * The row an overlay was opened from may be gone by the time it closes — the very action the
   * overlay performed can remove it. Focusing a detached element silently does nothing and leaves
   * focus on `<body>`, so the guard is what stops the restore from being a no-op nobody notices.
   */
  it('does not throw when the opener left the document while the overlay was open', async () => {
    await openOverlay();

    opener.remove();

    expect(() => {
      host.open.set(false);
      fixture.detectChanges();
    }).not.toThrow();
  });

  it('does nothing when the component has no panel', async () => {
    host.openEmpty.set(true);
    fixture.detectChanges();
    await fixture.whenStable();

    // No panel to move to, and — the part that matters — no crash that would take the overlay with
    // it. Focus simply never left the opener.
    expect(document.activeElement).toBe(opener);
  });
});
