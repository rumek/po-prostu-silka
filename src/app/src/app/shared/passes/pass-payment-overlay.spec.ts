import { Component, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MembershipPassView } from '../../core/admin/member-admin.models';
import { clubToday } from '../../core/passes/club-today';
import { PassPaymentOverlay } from './pass-payment-overlay';

const PASS: MembershipPassView = {
  id: 'p1',
  typeName: 'Karnet 8 wejść',
  validFrom: '2026-09-01',
  validTo: '2026-09-30',
  entryCount: 8,
  entriesUsed: 0,
  entriesLeft: 8,
  issuedAt: '2026-09-01T08:00:00+00:00',
  coversToday: false,
  paidAt: null,
};

@Component({
  imports: [PassPaymentOverlay],
  template: `
    <app-pass-payment-overlay
      [pass]="pass"
      [failure]="failure()"
      (confirmed)="confirmed.push($event)"
      (closed)="closed = closed + 1"
    />
  `,
})
class Host {
  readonly pass = PASS;
  readonly failure = signal<string | null>(null);
  readonly confirmed: string[] = [];
  closed = 0;
}

describe('PassPaymentOverlay', () => {
  let fixture: ComponentFixture<Host>;

  function element(): HTMLElement {
    return fixture.nativeElement as HTMLElement;
  }

  beforeEach(() => {
    fixture = TestBed.createComponent(Host);
    fixture.detectChanges();
  });

  it('defaults the day to the club-local today and refuses later days', async () => {
    await fixture.whenStable();
    fixture.detectChanges();

    const input = element().querySelector<HTMLInputElement>('input[type="date"]')!;
    expect(input.value).toBe(clubToday());
    expect(input.max).toBe(clubToday());
  });

  it('emits the chosen day on Zapisz', async () => {
    await fixture.whenStable();
    element().querySelector('form')!.dispatchEvent(new Event('submit'));

    expect(fixture.componentInstance.confirmed).toEqual([clubToday()]);
  });

  it('shows a refusal as a banner inside the panel', () => {
    fixture.componentInstance.failure.set('Data płatności nie może być w przyszłości.');
    fixture.detectChanges();

    expect(element().querySelector('.overlay-panel .alert')?.textContent).toContain(
      'Data płatności',
    );
  });

  it('closes from the X', () => {
    element().querySelector<HTMLButtonElement>('.overlay-close')!.click();

    expect(fixture.componentInstance.closed).toBe(1);
  });
});
