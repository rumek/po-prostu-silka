---
change_id: role-based-visibility
title: Per-role feature visibility and a main menu that reaches every page
status: implementing
created: 2026-09-22
updated: 2026-09-22
archived_at: null
---

## Notes

User's request (verbatim, Polish):

> nowy slice - widocznosc funkcjonalnosci. Uzytkownik powinien widziec swoj plan, swoje zajecia,
> swoj karnet, swoj profil ale nie powinien widziec grafiku calej silowni. Trener nie powinien miec
> swojego planu treningowego, powinien widziec grafik tylko ze swoimi zajeciami. Nie ma tez sensu
> wyswietlanie jego grafiku. Admin powinien miec swojego planu, w grafiku widzi wszystkie zajecia.
> Na stronie glownej nie powinny mu sie wyswietlac wszystkie nadchodzace, wymagane uwagi - chyba ze
> jest do nich faktycznie przypisany (jako prowadzacy albo uczestnik). Nie ma tez sensu jego karnet.
> Do kazdej strony powinno dac sie dostac z menu glownego. W tym momencie do stron admina nie ma
> linkow bezposrednio - przeanalizuj pod tym katem.

Working interpretation (to confirm during research/plan):

- **Member** — sees own training plan, own classes, own membership pass, own profile; does NOT see
  the whole-gym schedule.
- **Trainer** — no own training plan, no own pass; schedule shows only the classes they instruct.
  "Nie ma też sensu wyświetlanie jego grafiku" — ambiguous: likely a redundant personal
  "my schedule/my classes" view duplicating the filtered schedule.
- **Admin** — "Admin powinien mieć swojego planu" is read as a typo for "nie powinien mieć"
  (consistent with "nie ma też sensu jego karnet"); schedule shows all classes; dashboard shows
  upcoming / needs-attention items only where the admin is actually assigned (as instructor or
  participant).
- **Navigation** — every page reachable from the main menu; admin pages currently have no direct
  links.
