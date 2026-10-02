import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ScheduledClass } from '../../core/scheduling/class.models';
import { MakeupItem } from '../../core/scheduling/makeup.models';
import { MakeupClassPicker } from './makeup-class-picker';

const ITEM: MakeupItem = {
  absenceBookingId: 'a1',
  memberId: 'm1',
  displayName: 'Anna Kowalska',
  className: 'Joga',
  absenceStartsAt: '2026-09-28T08:00:00Z',
  instructor: 'Ola',
  deadline: '2026-10-28',
  status: 'open',
  closedByHand: false,
  makeup: null,
};

const PILATES: ScheduledClass = {
  id: 'c2',
  classGroupId: 'g2',
  name: 'Pilates',
  description: null,
  startsAt: '2026-10-05T17:00:00Z',
  durationMinutes: 60,
  instructorMemberId: 't2',
  instructor: 'Ewa',
  capacity: 10,
  freeSpots: 3,
  status: 'Scheduled',
};

/** The makeup class picker (S-36). */
describe('MakeupClassPicker', () => {
  let fixture: ComponentFixture<MakeupClassPicker>;
  let controller: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [MakeupClassPicker],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });

    controller = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(MakeupClassPicker);
    fixture.componentRef.setInput('item', ITEM);
    fixture.detectChanges();
  });

  afterEach(() => controller.verify());

  async function settle(): Promise<void> {
    await fixture.whenStable();
    fixture.detectChanges();
  }

  function root(): HTMLElement {
    return fixture.nativeElement as HTMLElement;
  }

  function buttonWith(text: string): HTMLButtonElement | undefined {
    return [...root().querySelectorAll('button')].find((button) =>
      button.textContent?.includes(text),
    );
  }

  it('lists the eligible classes with time, trainer and free spots', async () => {
    controller.expectOne('/api/makeups/a1/classes').flush([PILATES]);
    await settle();

    const text = root().textContent!;
    expect(text).toContain('Odrabianie: Anna Kowalska');
    expect(text).toContain('termin do 28 października');
    expect(text).toContain('Pilates');
    expect(text).toContain('Ewa');
    expect(text).toContain('wolne: 3');
  });

  it('books the chosen class and hands the item back', async () => {
    controller.expectOne('/api/makeups/a1/classes').flush([PILATES]);
    await settle();

    const booked = vi.fn();
    fixture.componentInstance.booked.subscribe(booked);

    buttonWith('Zapisz')!.click();
    await settle();

    const request = controller.expectOne('/api/makeups/a1/booking');
    expect(request.request.method).toBe('POST');
    expect(request.request.body).toEqual({ classId: 'c2' });
    const answered = { ...ITEM, status: 'planned' as const };
    request.flush(answered);
    await settle();

    expect(booked).toHaveBeenCalledWith(answered);
  });

  it('shows a refusal as a banner and reloads the classes', async () => {
    controller.expectOne('/api/makeups/a1/classes').flush([PILATES]);
    await settle();

    buttonWith('Zapisz')!.click();
    await settle();

    controller
      .expectOne('/api/makeups/a1/booking')
      .flush({ reason: 'class_full' }, { status: 409, statusText: 'Conflict' });
    await settle();

    expect(root().querySelector('.alert')!.textContent).toContain('nie ma już wolnych miejsc');
    controller.expectOne('/api/makeups/a1/classes').flush([]);
    await settle();
  });

  it('hands an item that stopped being open back to the list, without a banner or a reload', async () => {
    controller.expectOne('/api/makeups/a1/classes').flush([PILATES]);
    await settle();

    const stale = vi.fn();
    fixture.componentInstance.stale.subscribe(stale);

    buttonWith('Zapisz')!.click();
    await settle();

    controller
      .expectOne('/api/makeups/a1/booking')
      .flush({ reason: 'makeup_not_open' }, { status: 409, statusText: 'Conflict' });
    await settle();

    expect(stale).toHaveBeenCalledWith(expect.stringContaining('lista była nieaktualna'));
    expect(root().querySelector('.alert')).toBeNull();
    controller.expectNone('/api/makeups/a1/classes');
  });

  it('says so when no class fits the deadline', async () => {
    controller.expectOne('/api/makeups/a1/classes').flush([]);
    await settle();

    expect(root().textContent).toContain('Brak zajęć z wolnym miejscem w terminie odrabiania.');
  });
});
