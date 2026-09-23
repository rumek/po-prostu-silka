import { Component, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { useOverlayHistory } from './overlay-history';

@Component({
  selector: 'app-test-overlay',
  template: '<div class="overlay-panel"></div>',
})
class TestOverlay {
  readonly onBack = vi.fn();
  private readonly history = useOverlayHistory(() => this.onBack());
}

@Component({
  imports: [TestOverlay],
  template: `
    @if (open()) {
      <app-test-overlay />
    }
    @if (second()) {
      <app-test-overlay />
    }
  `,
})
class Host {
  readonly open = signal(false);
  /** A second overlay, opened in the same tick the first closes: the admin's actions → bookings. */
  readonly second = signal(false);
}

function popstate(): Promise<void> {
  return new Promise((resolve) =>
    window.addEventListener('popstate', () => resolve(), { once: true }),
  );
}

/**
 * Back closes an open overlay instead of leaving the screen (mobile-native-feel), against jsdom's
 * real History: one entry per open, popped exactly once however the overlay closes.
 */
describe('useOverlayHistory', () => {
  let fixture: ComponentFixture<Host>;

  beforeEach(() => {
    TestBed.configureTestingModule({ imports: [Host], providers: [provideRouter([])] });
    fixture = TestBed.createComponent(Host);
    fixture.detectChanges();
  });

  function overlay(): TestOverlay {
    return fixture.debugElement.children[0].componentInstance as TestOverlay;
  }

  it('pushes one same-URL entry on open, keeping the router state', async () => {
    window.history.replaceState({ navigationId: 3 }, '');
    const url = window.location.href;
    // A spy rather than history.length: jsdom's session history is shared across spec files, and a
    // pending pop from elsewhere shifts the length under the assertion.
    const push = vi.spyOn(window.history, 'pushState');

    fixture.componentInstance.open.set(true);
    fixture.detectChanges();

    expect(push).toHaveBeenCalledTimes(1);
    expect(push.mock.calls[0][2]).toBeUndefined();
    expect(window.location.href).toBe(url);
    expect(window.history.state).toMatchObject({ navigationId: 3, overlay: expect.any(String) });

    // Closed here rather than by teardown, so its pop cannot land in the next test.
    const popped = popstate();
    fixture.componentInstance.open.set(false);
    fixture.detectChanges();
    await popped;
  });

  it('closes the overlay when back pops its entry, without popping again', async () => {
    fixture.componentInstance.open.set(true);
    fixture.detectChanges();
    const onBack = overlay().onBack;
    const back = vi.spyOn(window.history, 'back');

    const popped = popstate();
    window.history.go(-1);
    await popped;
    expect(onBack).toHaveBeenCalledTimes(1);

    fixture.componentInstance.open.set(false);
    fixture.detectChanges();
    expect(back).not.toHaveBeenCalled();
  });

  it('pops its own entry exactly once when closed another way', async () => {
    fixture.componentInstance.open.set(true);
    fixture.detectChanges();
    const onBack = overlay().onBack;
    const token = (window.history.state as { overlay: string }).overlay;
    const back = vi.spyOn(window.history, 'back');

    const popped = popstate();
    fixture.componentInstance.open.set(false);
    fixture.detectChanges();
    await popped;

    expect(back).toHaveBeenCalledTimes(1);
    expect(onBack).not.toHaveBeenCalled();
    expect((window.history.state as { overlay?: string } | null)?.overlay).not.toBe(token);
  });
  /**
   * An overlay SWAP: the first overlay's pop is asynchronous, so it lands after the second overlay
   * opened. It must not be read as a back gesture that closes the second one.
   */
  it('keeps an overlay opened in the same tick another one closed', async () => {
    fixture.componentInstance.open.set(true);
    fixture.detectChanges();

    const popped = popstate();
    fixture.componentInstance.open.set(false);
    fixture.componentInstance.second.set(true);
    fixture.detectChanges();
    const second = overlay();
    await popped;
    await new Promise((resolve) => setTimeout(resolve));

    expect(second.onBack).not.toHaveBeenCalled();
    expect((window.history.state as { overlay?: string } | null)?.overlay).toEqual(
      expect.any(String),
    );

    // And back still closes it once it is open.
    const back = popstate();
    window.history.go(-1);
    await back;
    expect(second.onBack).toHaveBeenCalledTimes(1);

    fixture.componentInstance.second.set(false);
    fixture.detectChanges();
  });
});
