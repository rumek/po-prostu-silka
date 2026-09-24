import { TestBed } from '@angular/core/testing';
import { signal } from '@angular/core';
import { InstallService } from '../../core/pwa/install.service';
import { InstallPrompt } from './install-prompt';

const STORAGE_KEY = 'pps.install-prompt.dismissed-at';

/**
 * Same two promises as the push prompt: nothing on a browser that cannot install, and "Nie teraz"
 * that survives the next navigation.
 */
describe('InstallPrompt', () => {
  function configure(
    canInstall = signal(true),
    install = vi.fn().mockResolvedValue(true),
    needsManualInstall = signal(false),
  ) {
    TestBed.configureTestingModule({
      imports: [InstallPrompt],
      providers: [
        {
          provide: InstallService,
          useValue: { canInstall, install, needsManualInstall } as unknown as InstallService,
        },
      ],
    });

    return { canInstall, install };
  }

  async function render() {
    const fixture = TestBed.createComponent(InstallPrompt);
    await fixture.whenStable();

    return fixture;
  }

  function prompt(fixture: { nativeElement: unknown }): HTMLElement | null {
    return (fixture.nativeElement as HTMLElement).querySelector('.install-prompt');
  }

  function click(fixture: { nativeElement: unknown }, label: string): void {
    const button = Array.from(
      (fixture.nativeElement as HTMLElement).querySelectorAll('button'),
    ).find((b) => b.textContent?.trim().startsWith(label));

    expect(button).toBeDefined();
    button!.click();
  }

  beforeEach(() => localStorage.removeItem(STORAGE_KEY));
  afterEach(() => localStorage.removeItem(STORAGE_KEY));

  it('offers the install when the browser handed one over', async () => {
    configure();

    expect(prompt(await render())).not.toBeNull();
  });

  // Safari, Firefox, an installed app: no event, and no button that could help.
  it('renders nothing without an install event', async () => {
    configure(signal(false));

    expect(prompt(await render())).toBeNull();
  });

  it('replays the browser prompt on "Zainstaluj"', async () => {
    const { install, canInstall } = configure();

    const fixture = await render();
    click(fixture, 'Zainstaluj');
    await fixture.whenStable();

    expect(install).toHaveBeenCalledOnce();

    // The service drops the spent event; the banner reads that and withdraws.
    canInstall.set(false);
    await fixture.whenStable();

    expect(prompt(fixture)).toBeNull();
  });

  it('hides on "Nie teraz" and records the dismissal', async () => {
    configure();

    const fixture = await render();
    click(fixture, 'Nie teraz');
    await fixture.whenStable();

    expect(prompt(fixture)).toBeNull();
    expect(localStorage.getItem(STORAGE_KEY)).not.toBeNull();
  });

  it('stays hidden for a member who dismissed it recently', async () => {
    localStorage.setItem(STORAGE_KEY, new Date().toISOString());
    configure();

    expect(prompt(await render())).toBeNull();
  });

  it('asks again once the dismissal has aged out', async () => {
    localStorage.setItem(
      STORAGE_KEY,
      new Date(Date.now() - 60 * 24 * 60 * 60 * 1000).toISOString(),
    );
    configure();

    expect(prompt(await render())).not.toBeNull();
  });

  // iOS never fires the event; the banner explains the share sheet, and offers no install button
  // that could not work.
  it('explains the share sheet on iOS instead of offering a button', async () => {
    configure(signal(false), undefined, signal(true));

    const fixture = await render();

    expect(prompt(fixture)?.textContent).toContain('Do ekranu początkowego');
    expect(
      Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('button')).map((b) =>
        b.textContent?.trim(),
      ),
    ).toEqual(['Nie teraz']);
  });

  it('honours "Nie teraz" on iOS too', async () => {
    configure(signal(false), undefined, signal(true));

    const fixture = await render();
    click(fixture, 'Nie teraz');
    await fixture.whenStable();

    expect(prompt(fixture)).toBeNull();
    expect(localStorage.getItem(STORAGE_KEY)).not.toBeNull();
  });
});
