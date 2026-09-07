import { ComponentFixture, TestBed } from '@angular/core/testing';
import { PlanSummary } from './plan-summary';

/**
 * The shared plan-identity block used by `/my-plan` and, from S-12, the dashboard's plan card.
 *
 * The heading level is what these tests protect. It is the one input that exists purely to keep the
 * document outline correct in the second caller, so a regression here is invisible on screen and
 * only shows up to someone navigating by heading.
 */
describe('PlanSummary', () => {
  let fixture: ComponentFixture<PlanSummary>;

  function create(headingLevel?: 'h1' | 'h2') {
    TestBed.configureTestingModule({ imports: [PlanSummary] });

    fixture = TestBed.createComponent(PlanSummary);
    fixture.componentRef.setInput('name', 'Masa - jesień');
    fixture.componentRef.setInput('assignedByDisplayName', 'Marek Trener');
    fixture.componentRef.setInput('createdAt', new Date('2026-09-01T10:00').toISOString());

    if (headingLevel) {
      fixture.componentRef.setInput('headingLevel', headingLevel);
    }

    fixture.detectChanges();
  }

  function element(): HTMLElement {
    return fixture.nativeElement as HTMLElement;
  }

  it('renders the plan name and who assigned it', () => {
    create();

    expect(element().textContent).toContain('Masa - jesień');
    expect(element().textContent).toContain('Marek Trener');
  });

  /** The page-heading case: on `/my-plan` the plan name IS the page title. */
  it('renders an h1 by default', () => {
    create();

    expect(element().querySelector('h1')).not.toBeNull();
    expect(element().querySelector('h2')).toBeNull();
  });

  /** The card case: under a dashboard's own h1, a second h1 would flatten the outline. */
  it('renders an h2 when asked', () => {
    create('h2');

    expect(element().querySelector('h2')).not.toBeNull();
    expect(element().querySelector('h1')).toBeNull();
  });
});
