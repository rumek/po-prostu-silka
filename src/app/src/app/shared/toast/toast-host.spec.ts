import { LiveAnnouncer } from '@angular/cdk/a11y';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ToastHost } from './toast-host';
import { ToastService } from './toast.service';

/**
 * The third outlet of the failure rule. Three things here are load-bearing and none of them is
 * visible in a screenshot: that an error does NOT time out, that a confirmation does, and that the
 * words reach a screen reader at all — the host itself is aria-hidden, so `LiveAnnouncer` is the
 * only path to one.
 */
describe('ToastHost', () => {
  let fixture: ComponentFixture<ToastHost>;
  let toasts: ToastService;
  let announce: ReturnType<typeof vi.fn>;

  beforeEach(() => {
    vi.useFakeTimers();
    announce = vi.fn().mockResolvedValue(undefined);

    TestBed.configureTestingModule({
      imports: [ToastHost],
      providers: [{ provide: LiveAnnouncer, useValue: { announce } }],
    });

    toasts = TestBed.inject(ToastService);
    fixture = TestBed.createComponent(ToastHost);
    fixture.detectChanges();
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  function text(): string {
    return (fixture.nativeElement as HTMLElement).textContent ?? '';
  }

  function toastElements(): HTMLElement[] {
    return Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('.toast'));
  }

  it('renders nothing until something is raised', () => {
    expect(toastElements()).toHaveLength(0);
  });

  it.each([
    ['success' as const, 'toast--success'],
    ['info' as const, 'toast--info'],
    ['error' as const, 'toast--error'],
  ])('renders a %s toast with its own tone class', (tone, className) => {
    toasts[tone]('Coś się wydarzyło.');
    fixture.detectChanges();

    expect(text()).toContain('Coś się wydarzyło.');
    expect(toastElements()[0].classList).toContain(className);
  });

  it('stacks several toasts at once', () => {
    toasts.success('Pierwszy.');
    toasts.error('Drugi.');
    fixture.detectChanges();

    expect(toastElements()).toHaveLength(2);
  });

  /**
   * A CONFIRMATION MAY VANISH; A REFUSAL MAY NOT. The thing a success toast reports is visible on
   * the screen behind it. A refusal is not — the row still looks exactly as it did before the user
   * acted, so the toast is the only copy of the explanation.
   */
  it('auto-dismisses success and info', () => {
    toasts.success('Zapisano.');
    toasts.info('Odświeżono.');
    fixture.detectChanges();
    expect(toastElements()).toHaveLength(2);

    vi.advanceTimersByTime(5000);
    fixture.detectChanges();

    expect(toastElements()).toHaveLength(0);
  });

  it('keeps an error until it is dismissed', () => {
    toasts.error('Nie udało się.');
    fixture.detectChanges();

    vi.advanceTimersByTime(60_000);
    fixture.detectChanges();

    expect(toastElements()).toHaveLength(1);

    toastElements()[0].querySelector<HTMLButtonElement>('.toast-dismiss')?.click();
    fixture.detectChanges();

    expect(toastElements()).toHaveLength(0);
  });

  it('dismisses only the toast that was clicked', () => {
    toasts.error('Pierwszy.');
    toasts.error('Drugi.');
    fixture.detectChanges();

    toastElements()[0].querySelector<HTMLButtonElement>('.toast-dismiss')?.click();
    fixture.detectChanges();

    expect(toastElements()).toHaveLength(1);
    expect(text()).toContain('Drugi.');
    expect(text()).not.toContain('Pierwszy.');
  });

  /**
   * THE HOST IS aria-hidden ON PURPOSE — see the component docblock. That makes the announcer the
   * only thing standing between a screen-reader user and silence, so it is asserted directly.
   */
  it('announces every toast through LiveAnnouncer', () => {
    toasts.success('Zapisano zmiany.');
    fixture.detectChanges();

    expect(announce).toHaveBeenCalledWith('Zapisano zmiany.', 'polite');
  });

  it('announces a refusal assertively, because it answers something the user just did', () => {
    toasts.error('Nie udało się zapisać.');
    fixture.detectChanges();

    expect(announce).toHaveBeenCalledWith('Nie udało się zapisać.', 'assertive');
  });

  it('announces each toast once, however often the host re-renders', () => {
    toasts.info('Lista była nieaktualna — odświeżono.');
    fixture.detectChanges();
    fixture.detectChanges();
    fixture.detectChanges();

    expect(announce).toHaveBeenCalledTimes(1);
  });

  it('keeps the visual host out of the accessibility tree', () => {
    const host = (fixture.nativeElement as HTMLElement).querySelector('.toast-host');

    expect(host?.getAttribute('aria-hidden')).toBe('true');
  });
});
