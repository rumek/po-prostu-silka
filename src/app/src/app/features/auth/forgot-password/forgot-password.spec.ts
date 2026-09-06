import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { ForgotPassword } from './forgot-password';

describe('ForgotPassword', () => {
  let fixture: ComponentFixture<ForgotPassword>;
  let controller: HttpTestingController;

  beforeEach(async () => {
    TestBed.configureTestingModule({
      imports: [ForgotPassword],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    });

    controller = TestBed.inject(HttpTestingController);

    fixture = TestBed.createComponent(ForgotPassword);
    await fixture.whenStable();
  });

  afterEach(() => controller.verify());

  function compiled(): HTMLElement {
    return fixture.nativeElement as HTMLElement;
  }

  function fill(email: string): void {
    const input = compiled().querySelector<HTMLInputElement>('#email')!;
    input.value = email;
    input.dispatchEvent(new Event('input'));
  }

  function submit(): void {
    compiled().querySelector('form')!.dispatchEvent(new Event('submit'));
  }

  /** vi.waitFor because the submit handler is async — the request lands a microtask later. */
  function expectRequest() {
    return vi.waitFor(() => controller.expectOne('/api/auth/forgot-password'));
  }

  it('sends the trimmed address', async () => {
    fill('  anna@test.local  ');
    submit();

    const request = await expectRequest();

    expect(request.request.body).toEqual({ email: 'anna@test.local' });
    request.flush(null);
  });

  it('replaces the form with the neutral confirmation after submitting', async () => {
    fill('anna@test.local');
    submit();
    (await expectRequest()).flush(null);

    await fixture.whenStable();
    fixture.detectChanges();

    expect(compiled().querySelector('form')).toBeNull();
    expect(compiled().querySelector('.notice')!.textContent).toContain('Jeśli konto');
  });

  /**
   * THE NON-DISCLOSURE TEST. The API answers identically for a registered and an unregistered
   * address, and the screen must not undo that. If a "no such address" branch ever appears here,
   * this fails — read the comment on the component before changing it.
   */
  it('shows the same confirmation whatever the address is', async () => {
    fill('nobody@test.local');
    submit();
    (await expectRequest()).flush(null);

    await fixture.whenStable();
    fixture.detectChanges();
    const unknown = compiled().querySelector('.notice')!.textContent;

    // A second component instance, a registered address, same assertion target.
    const second = TestBed.createComponent(ForgotPassword);
    await second.whenStable();

    const element = second.nativeElement as HTMLElement;
    const input = element.querySelector<HTMLInputElement>('#email')!;
    input.value = 'anna@test.local';
    input.dispatchEvent(new Event('input'));
    element.querySelector('form')!.dispatchEvent(new Event('submit'));

    (await vi.waitFor(() => controller.expectOne('/api/auth/forgot-password'))).flush(null);
    await second.whenStable();
    second.detectChanges();

    expect(element.querySelector('.notice')!.textContent).toBe(unknown);
  });

  it('does not submit an invalid address', () => {
    fill('nie-adres');
    submit();

    controller.expectNone('/api/auth/forgot-password');

    fixture.detectChanges();
    expect(compiled().querySelector('.field-error')).not.toBeNull();
  });

  /** A 429 from the rate limiter says nothing about the account — the message is about the request. */
  it('reports a failed request without mentioning the account', async () => {
    fill('anna@test.local');
    submit();

    (await expectRequest()).flush(null, { status: 429, statusText: 'Too Many Requests' });

    await fixture.whenStable();
    fixture.detectChanges();

    expect(compiled().querySelector('form')).not.toBeNull();
    expect(compiled().querySelector('.alert')!.textContent).toContain('Nie udało się wysłać');
  });
});
