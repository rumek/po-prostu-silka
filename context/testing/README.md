---
project: "Po Prostu Siłka"
document: manual-test-plan
status: draft
created: 2026-09-28
scope: stan aplikacji po S-27 (obecność) i w trakcie S-28; S-29 (RODO) jeszcze nie zbudowane
---

# Plan testów manualnych — Po Prostu Siłka

Plan dla testera manualnego, podzielony na kilka dokumentów. Zacznij od tego pliku i od
[środowiska i danych testowych](environment.md), a potem przechodź przez pliki z przypadkami.

Każdy przypadek testowy (TC) ma stały identyfikator (np. `AUTH-03`). Identyfikatorów nie zmieniamy
ani nie używamy ponownie, bo będą się do nich odwoływać raporty, niezależnie od tego, jaką formę
przyjmie feedback (arkusz, tracker zgłoszeń, formularz). Przypadki często odwołują się do siebie
nawzajem po ID. Po prefiksie w tabeli niżej znajdziesz plik z danym przypadkiem.

## Spis dokumentów

| Plik | Obszar | Prefiksy ID |
| --- | --- | --- |
| [environment.md](environment.md) | adres, konta testowe, ograniczenia środowiska, urządzenia i szerokości ekranu | — |
| [01-account-and-access.md](01-account-and-access.md) | logowanie i sesja, rejestracja z zaproszenia, hasło, nawigacja i uprawnienia person | `AUTH`, `REG`, `PWD`, `NAV` |
| [02-member-screens.md](02-member-screens.md) | ekran Start, Moje zajęcia i historia, Mój plan, Moje konto | `DASH`, `MBR` |
| [03-members-and-passes.md](03-members-and-passes.md) | lista członków, kartoteki, kody zaproszeń, role, blokowanie; karnety | `MEM`, `PASS` |
| [04-schedule-bookings-attendance.md](04-schedule-bookings-attendance.md) | typy zajęć, kalendarz administratora, grafik obsługi, zapisy, obecność | `TYPE`, `CLS`, `SCH`, `BOOK`, `ATT` |
| [05-exercises-and-plans.md](05-exercises-and-plans.md) | biblioteka ćwiczeń, kreator planów treningowych | `EX`, `PLAN` |
| [06-notifications-and-cross-cutting.md](06-notifications-and-cross-cutting.md) | e-mail i push, instalacja PWA, brak sieci, dostępność, wygląd | `NOTIF`, `PWA`, `X` |

## Czym jest aplikacja

„Po Prostu Siłka” to aplikacja webowa małego klubu fitness, zaprojektowana z myślą o telefonie
(działa też jako aplikacja instalowana na ekranie głównym, PWA). Łączy:

- **grafik zajęć grupowych** i **zapisy na zajęcia**. Zapisuje wyłącznie obsługa klubu: członek
  nigdy nie zapisuje się sam.
- **karnety**. Zapis jest możliwy tylko z ważnym karnetem, który ma wolne wejście.
- **obecność** na zajęciach. Wejście z karnetu zużywa obecność, a nieobecność je zwraca.
- **indywidualne plany treningowe** i **bibliotekę ćwiczeń** z instrukcjami i filmami z YouTube.
- **powiadomienia e-mail i push** o odwołaniu lub zmianie zajęć.

### Trzy role (persony)

Każde konto widzi aplikację jednej persony. Pierwszeństwo: **Administrator > Trener > Członek**.

| Persona | Co widzi w menu | Najważniejsze możliwości |
| --- | --- | --- |
| **Członek** | Start, Zajęcia, Plan, (Więcej) | podgląd swoich zajęć, historii obecności, karnetu i planu; profil; zmiana hasła |
| **Trener** | Start, Grafik, Członkowie, (Więcej) | grafik **tylko swoich** zajęć, zapisywanie członków na swoje zajęcia, obecność, plany treningowe członków |
| **Administrator** | Start, Grafik, Członkowie, Typy zajęć, Ćwiczenia | wszystko: członkowie, karnety, kody zaproszeń, role, typy zajęć, grafik, zapisy na dowolne zajęcia, ćwiczenia, plany |

Zasady, które warto mieć w głowie podczas testów:

- Trener i administrator **nie mają** karnetu, zapisów ani planu. Aplikacja odmawia ich nadania.
- Konto zakłada się **tylko z zaproszenia** (linku z kodem od klubu). Nie ma publicznej rejestracji.
- Klub może prowadzić kartotekę osoby **bez konta**. Po rejestracji z kodem konto „przejmuje” tę
  kartotekę razem z karnetem, zapisami i planem.
- Na zajęciach nigdy nie może być więcej zapisanych niż miejsc, także przy jednoczesnych zapisach.

## Jak czytać przypadki testowe

Każdy TC ma:

- **Priorytet:** **P1** — ścieżka krytyczna, błąd blokuje używanie aplikacji albo narusza regułę
  biznesową; **P2** — ważna funkcja lub walidacja; **P3** — kosmetyka, wygoda, rzadkie przypadki.
- **Rola**, **warunki wstępne**, **kroki** i **oczekiwany rezultat**.

Teksty w cudzysłowie to dokładne komunikaty z aplikacji. Inne brzmienie komunikatu też jest
błędem, zgłaszanym jako P3, chyba że komunikat wprowadza w błąd.

Proponowane statusy wyniku: **Zaliczony**, **Niezaliczony** (z opisem błędu), **Zablokowany**
(nie da się wykonać, np. przez inny błąd lub brak danych), **Pominięty** (świadomie, z powodem).

## Ścieżka krytyczna (szybki test regresji)

Po każdym wdrożeniu, w tej kolejności (ok. 45–60 min):

`AUTH-01` → `AUTH-02` → `NAV-01` → `NAV-02` → `REG-01` → `PASS-01` → `BOOK-01` → `BOOK-03` →
`BOOK-04` → `DASH-01` → `MBR-01` → `ATT-01` → `ATT-02` → `MBR-02` → `CLS-02` → `CLS-10` →
`NOTIF-01` → `PLAN-01` → `MBR-05` → `MBR-06` → `PWD-01` → `PWD-03` → `MEM-11` → `AUTH-07`

## Raportowanie (do ustalenia)

Forma feedbacku i odhaczania przypadków nie jest jeszcze wybrana. Niezależnie od niej przyda się:

- **Wynik per TC:** ID, status (Zaliczony / Niezaliczony / Zablokowany / Pominięty), data,
  urządzenie i przeglądarka, konto testowe, krótka uwaga.
- **Zgłoszenie błędu:** ID TC (jeśli dotyczy), tytuł, kroki odtworzenia, rezultat oczekiwany i
  faktyczny, priorytet (P1–P3), urządzenie/przeglądarka/szerokość ekranu, zrzut ekranu lub nagranie,
  godzina wystąpienia (pomaga odszukać logi serwera).
- **Uwagi spoza TC** (UX, niejasne teksty, pomysły) zbierane osobno od błędów.

Gdy forma zostanie ustalona, ta sekcja opisze ją dokładnie.

## Poza zakresem tego planu

- Obsługa danych osobowych (RODO): informacja o przetwarzaniu, usuwanie i anonimizacja członka.
  Funkcja jest zaplanowana (S-29), ale jeszcze nie zbudowana. Przypadki dopiszemy po wdrożeniu.
- Alerty, monitoring, kopie zapasowe i procedura wycofania wdrożenia (S-28): to testy operacyjne
  właściciela, nie testera aplikacji.
- Testy wydajnościowe i bezpieczeństwa wykraczające poza podstawowe obserwacje z `X-07`, `AUTH-02`
  i `PWD-02`.
- Bezpośrednie wywołania API (poza wpisywaniem adresów ekranów w przeglądarce).
