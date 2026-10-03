import { Component, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { RosterReport as Report, RosterSkip } from '../../core/scheduling/roster.models';
import { groupRosterLines } from './roster-lines';
import { RosterReport } from './roster-report';

function skip(memberName: string, reason: string, startsAt: string): RosterSkip {
  return { memberId: memberName, memberName, classId: startsAt, startsAt, reason };
}

@Component({
  imports: [RosterReport],
  template: `<app-roster-report [report]="report()" (dismissed)="dismissals = dismissals + 1" />`,
})
class Host {
  readonly report = signal<Report>({ booked: 0, skipped: [] });
  dismissals = 0;
}

describe('groupRosterLines', () => {
  it('folds a member and a reason into one line with every date, in first-seen order', () => {
    const lines = groupRosterLines([
      skip('Anna', 'no_valid_pass', '2026-11-04T17:00:00Z'),
      skip('Bartek', 'class_full', '2026-11-04T17:00:00Z'),
      skip('Anna', 'no_valid_pass', '2026-11-11T17:00:00Z'),
      skip('Anna', 'class_full', '2026-11-18T17:00:00Z'),
    ]);

    expect(lines.map((l) => [l.memberName, l.reason, l.startsAt.length])).toEqual([
      ['Anna', 'no_valid_pass', 2],
      ['Bartek', 'class_full', 1],
      ['Anna', 'class_full', 1],
    ]);
  });
});

describe('RosterReport', () => {
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

  it('renders nothing when nothing was skipped', () => {
    fixture.componentInstance.report.set({ booked: 6, skipped: [] });
    fixture.detectChanges();

    expect(compiled().querySelector('.roster-report')).toBeNull();
  });

  it('lists each member and reason once, with the dates and the booking table sentence', () => {
    fixture.componentInstance.report.set({
      booked: 2,
      skipped: [
        skip('Anna Kowalska', 'no_valid_pass', '2026-11-04T17:00:00Z'),
        skip('Anna Kowalska', 'no_valid_pass', '2026-11-11T17:00:00Z'),
      ],
    });
    fixture.detectChanges();

    const panel = compiled().querySelector('.roster-report');
    expect(panel?.getAttribute('role')).toBe('status');
    expect(panel?.textContent).toContain('Nie wszystkich zapisano');

    const lines = compiled().querySelectorAll('.roster-report-lines li');
    expect(lines).toHaveLength(1);
    expect(lines[0].textContent).toContain('Anna Kowalska');
    expect(lines[0].textContent).toContain('4.11');
    expect(lines[0].textContent).toContain('11.11');
    expect(lines[0].textContent).toContain('nie ma karnetu');
  });

  it('emits dismissed from its X', () => {
    fixture.componentInstance.report.set({
      booked: 0,
      skipped: [skip('Anna', 'class_full', '2026-11-04T17:00:00Z')],
    });
    fixture.detectChanges();

    compiled().querySelector<HTMLButtonElement>('button[aria-label="Zamknij"]')!.click();

    expect(fixture.componentInstance.dismissals).toBe(1);
  });
});
