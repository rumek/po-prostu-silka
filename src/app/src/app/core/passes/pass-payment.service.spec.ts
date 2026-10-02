import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { PassPaymentService } from './pass-payment.service';

describe('PassPaymentService', () => {
  let service: PassPaymentService;
  let controller: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });

    service = TestBed.inject(PassPaymentService);
    controller = TestBed.inject(HttpTestingController);
  });

  afterEach(() => controller.verify());

  it('puts an explicit day to the pass-scoped paid route', async () => {
    const result = service.setPaid('p/1', '2026-10-02');

    const request = controller.expectOne('/api/passes/p%2F1/paid');
    expect(request.request.method).toBe('PUT');
    expect(request.request.body).toEqual({ paidAt: '2026-10-02' });
    request.flush({ id: 'p/1', paidAt: '2026-10-02' });

    await expect(result).resolves.toMatchObject({ paidAt: '2026-10-02' });
  });

  /** Unpaid is an explicit null, never a toggle — two staff at once converge. */
  it('sends null to mark a karnet unpaid', async () => {
    const result = service.setPaid('p1', null);

    const request = controller.expectOne('/api/passes/p1/paid');
    expect(request.request.body).toEqual({ paidAt: null });
    request.flush({ id: 'p1', paidAt: null });

    await result;
  });

  it('rejects rather than swallowing a refusal', async () => {
    const result = service.setPaid('p1', '2099-01-01');

    controller
      .expectOne('/api/passes/p1/paid')
      .flush({ reason: 'invalid_paid_at' }, { status: 400, statusText: 'Bad Request' });

    await expect(result).rejects.toBeTruthy();
  });
});
