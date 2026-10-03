import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { TrainerGroups } from './trainer-groups';

describe('TrainerGroups', () => {
  let fixture: ComponentFixture<TrainerGroups>;
  let controller: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [TrainerGroups],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    });
    controller = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(TrainerGroups);
    fixture.detectChanges();
  });

  afterEach(() => controller.verify());

  async function settle(): Promise<void> {
    await fixture.whenStable();
    fixture.detectChanges();
  }

  function element(): HTMLElement {
    return fixture.nativeElement as HTMLElement;
  }

  it('lists the groups the trainer instructs, each leading to its roster', async () => {
    controller.expectOne('/api/trainer/groups').flush([
      {
        id: 'g1',
        name: 'Pon 18:00',
        rosterCount: 4,
        capacity: 6,
        nextClassAt: '2026-11-04T17:00:00Z',
      },
    ]);
    await settle();

    const row = element().querySelector('li[appRow]')!;
    expect(row.textContent).toContain('Pon 18:00');
    expect(row.textContent).toContain('skład 4/6');
    expect(row.querySelector('a')?.getAttribute('href')).toBe('/trainer/groups/g1');
  });

  it('says so when the trainer instructs no group right now', async () => {
    controller.expectOne('/api/trainer/groups').flush([]);
    await settle();

    expect(element().querySelector('app-empty')?.textContent).toContain(
      'Nie prowadzisz teraz zajęć żadnej grupy',
    );
  });

  it('shows a failed load as the screen state', async () => {
    controller
      .expectOne('/api/trainer/groups')
      .flush(null, { status: 500, statusText: 'Server Error' });
    await settle();

    expect(element().querySelector('.alert')).not.toBeNull();
  });
});
