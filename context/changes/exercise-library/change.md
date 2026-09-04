---
change_id: exercise-library
title: Admin-only exercise library with list, detail and create screens
status: implementing
created: 2026-09-04
updated: 2026-09-04
archived_at: null
---

## Notes

pozycja powinna byc dostepna tylko dla administratora w menu gornym (na razie, dopoki nie ma dashboardu dla admina i usera). Musi byc stworzony ekran listy zajec, detale cwiczenia i ekran dodawania. Na liscie bedzie nazwa, opis, grupa miesni i miniaturka pobrana z YT. W detalach jest opcja edytuj i wszystkie dane.

**Decision 2026-09-04:** the top-menu entry is dropped from this change. The library keeps the
existing convention — admin screens are reached by URL or from a cross-link on another admin screen,
with no global nav entry — until the admin dashboard (S-12) gives those entries a home.
