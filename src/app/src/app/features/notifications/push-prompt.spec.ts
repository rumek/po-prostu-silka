import { TestBed } from '@angular/core/testing';
import { signal } from '@angular/core';
import { PushService } from '../../core/notifications/push.service';
import { PushPrompt } from './push-prompt';

const STORAGE_KEY = 'pps.push-prompt.dismissed-at';

/**
 * The two things this component must never get wrong: it must not appear on a browser that cannot
 * do push, and "Nie teraz" must actually mean not now — a prompt that returns on the next
 * navigation is a nag, and the member's only remaining escape would be the browser's permanent
 * block.
 */
describe('PushPrompt', () => {
  function configure(push: Partial<PushService>) {
    TestBed.configureTestingModule({
      imports: [PushPrompt],
      providers: [{ provide: PushService, useValue: push as PushService }],
    });
  }

  /** Supported, not yet subscribed — the one state the prompt exists for. */
  function undecided(subscribe = vi.fn().mockResolvedValue(true)): Partial<PushService> {
    return {
      isSupported: signal(true),
      isSubscribed: signal(false),
      subscribe,
    } as unknown as Partial<PushService>;
  }

  async function render() {
    const fixture = TestBed.createComponent(PushPrompt);
    await fixture.whenStable();

    return fixture;
  }

  function prompt(fixture: { nativeElement: unknown }): HTMLElement | null {
    return (fixture.nativeElement as HTMLElement).querySelector('.push-prompt');
  }

  function buttons(fixture: { nativeElement: unknown }): HTMLButtonElement[] {
    return Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('button'));
  }

  function click(fixture: { nativeElement: unknown }, label: string): void {
    const button = buttons(fixture).find((b) => b.textContent?.trim().startsWith(label));

    expect(button).toBeDefined();
    button!.click();
  }

  beforeEach(() => localStorage.removeItem(STORAGE_KEY));
  afterEach(() => localStorage.removeItem(STORAGE_KEY));

  it('asks an undecided member', async () => {
    configure(undecided());

    const fixture = await render();

    expect(prompt(fixture)).not.toBeNull();
  });

  // Desktop Safari, a non-installed iPhone, a dev build with the worker off, a server without VAPID
  // keys. None of them is the member's problem, and none of them has a button that would help.
  it('renders nothing when push is unsupported', async () => {
    configure({
      isSupported: signal(false),
      isSubscribed: signal(false),
      subscribe: vi.fn(),
    } as unknown as Partial<PushService>);

    const fixture = await render();

    expect(prompt(fixture)).toBeNull();
  });

  it('renders nothing to a member who already subscribed', async () => {
    configure({
      isSupported: signal(true),
      isSubscribed: signal(true),
      subscribe: vi.fn(),
    } as unknown as Partial<PushService>);

    const fixture = await render();

    expect(prompt(fixture)).toBeNull();
  });

  it('subscribes on "Włącz" and then stops asking', async () => {
    const subscribe = vi.fn().mockResolvedValue(true);
    const push = undecided(subscribe);
    configure(push);

    const fixture = await render();
    click(fixture, 'Włącz');
    await fixture.whenStable();

    expect(subscribe).toHaveBeenCalledOnce();

    // The service flips isSubscribed in real life; here the signal is ours to flip, which is what
    // the component is supposed to be reading.
    (push.isSubscribed as unknown as ReturnType<typeof signal<boolean>>).set(true);
    await fixture.whenStable();

    expect(prompt(fixture)).toBeNull();
  });

  // A failed subscribe means the browser's own prompt was declined, or there is no worker, or the
  // server has no keys. Offering the button again would offer something that can no longer work.
  it('withdraws when the subscribe attempt fails', async () => {
    configure(undecided(vi.fn().mockResolvedValue(false)));

    const fixture = await render();
    click(fixture, 'Włącz');
    await fixture.whenStable();

    expect(prompt(fixture)).toBeNull();
  });

  it('hides on "Nie teraz" and records the dismissal', async () => {
    configure(undecided());

    const fixture = await render();
    click(fixture, 'Nie teraz');
    await fixture.whenStable();

    expect(prompt(fixture)).toBeNull();
    expect(localStorage.getItem(STORAGE_KEY)).not.toBeNull();
  });

  // The component is re-created on every navigation through the shell. In memory the dismissal
  // would last until the next route change, which is not a dismissal.
  it('stays hidden for a member who dismissed it recently', async () => {
    localStorage.setItem(STORAGE_KEY, new Date().toISOString());
    configure(undecided());

    expect(prompt(await render())).toBeNull();
  });

  // "Nie teraz", not "nigdy". A member who declined before they had booked anything is asked once
  // more when the app has become useful to them.
  it('asks again once the dismissal has aged out', async () => {
    const longAgo = new Date(Date.now() - 30 * 24 * 60 * 60 * 1000).toISOString();
    localStorage.setItem(STORAGE_KEY, longAgo);
    configure(undecided());

    expect(prompt(await render())).not.toBeNull();
  });

  // Someone else's key, or an older format. Asking once more is a smaller failure than never
  // asking again.
  it('treats an unreadable dismissal as no dismissal', async () => {
    localStorage.setItem(STORAGE_KEY, 'not-a-date');
    configure(undecided());

    expect(prompt(await render())).not.toBeNull();
  });
});
