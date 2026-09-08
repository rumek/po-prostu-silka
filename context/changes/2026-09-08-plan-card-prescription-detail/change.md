---
change_id: plan-card-prescription-detail
title: The plan card carries the whole prescription
status: implementing
created: 2026-09-08
updated: 2026-09-08
---

## Notes

S-15, jedyny slice M-3. Karta ćwiczenia na ekranie planu ma nieść całą receptę trenera:
czas trwania dla ćwiczeń mierzonych czasem (deska), partię mięśniową, oddzielną ikonkę (i)
do opisu ćwiczenia zamiast nazwy-linku, oraz notatkę trenera wydzieloną tłem i ikonką.

Czas trwania mieszka na `TrainingPlanItem`, obok `WeightKg` i `RestSeconds` — jest receptą
per-plan, nie cechą ćwiczenia. Ta sama deska to 45 s w jednym planie i 60 s w innym.
