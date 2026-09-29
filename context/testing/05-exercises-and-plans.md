# Biblioteka ćwiczeń i plany treningowe

Konwencje (priorytety, statusy, format przypadku) i dane testowe: [README](README.md), [środowisko i dane testowe](environment.md).

## Biblioteka ćwiczeń i plany treningowe (EX, PLAN)

### EX-01 · Lista ćwiczeń — P2
**Rola:** administrator
**Oczekiwany rezultat:** kafelki z miniaturą filmu (albo ikoną odtwarzania, gdy filmu nie ma),
nazwą, grupą mięśniową i opisem; w nagłówku liczba ćwiczeń. Zaznaczenie „Pokaż nieaktywne” pokazuje
też nieaktywne, z oznaczeniem.

### EX-02 · Dodanie ćwiczenia z samą nazwą — P1
**Kroki:** „Dodaj ćwiczenie” → tylko nazwa → „Zapisz”.
**Oczekiwany rezultat:** ćwiczenie powstaje; na ekranie szczegółu widać „To ćwiczenie ma na razie
tylko nazwę. Użyj „Edytuj”, żeby dodać instrukcje i film.”.

### EX-03 · Pełne ćwiczenie z filmem — P1
**Kroki:** wypełnij wszystkie pola (opis, grupa mięśniowa z podpowiedzi, trudność, sprzęt,
przygotowanie, pozycja wyjściowa, wykonanie) i link do filmu YouTube. Powtórz z różnymi postaciami
linku: `https://www.youtube.com/watch?v=…`, `https://youtu.be/…`, link z przycisku „Udostępnij” (z
`?si=`), link do Shorts.
**Oczekiwany rezultat:** wszystkie postacie są przyjęte, a po zapisie link ma jednolitą postać. Na
ekranie szczegółu film się odtwarza, a miniatura pojawia się na liście.

### EX-04 · Walidacja ćwiczenia — P2
**Kroki:** zapisz bez nazwy; z nazwą istniejącego aktywnego ćwiczenia; z linkiem niebędącym YouTube
(np. `https://vimeo.com/123`) lub z tekstem `abc`.
**Oczekiwany rezultat:** „Podaj nazwę ćwiczenia.”, „Ta nazwa jest już zajęta przez inne aktywne
ćwiczenie. Wybierz inną.”, „Podaj poprawny link do filmu na YouTube.”.

### EX-05 · Dezaktywacja ćwiczenia — P2
**Kroki:** dezaktywuj ćwiczenie, które jest w czyimś planie; sprawdź plan członka i kreator planu;
utwórz nowe ćwiczenie o tej samej nazwie; aktywuj stare.
**Oczekiwany rezultat:** ćwiczenie znika z listy i z biblioteki w kreatorze, ale istniejący plan
członka dalej je pokazuje. Nazwa jest zwolniona, więc nowe ćwiczenie o tej nazwie da się zapisać.
Aktywacja starego przy zajętej nazwie powinna się nie udać z czytelnym komunikatem.

### PLAN-01 · Ułożenie nowego planu — P1
**Rola:** trener (Członkowie → imię osoby) lub administrator (Członkowie → menu → „Plan”)
**Warunki wstępne:** członek bez planu.
**Kroki:**
1. Wpisz nazwę planu.
2. W „Biblioteka ćwiczeń” wyszukaj i dodaj 3–4 ćwiczenia.
3. Wypełnij parametry (serie, powtórzenia np. `8-12`, ciężar, czas, przerwa) i notatkę przy jednym.
4. Zmień kolejność, przeciągając wiersze (uchwyt), także na telefonie.
5. Kliknij „Przypisz plan”.

**Oczekiwany rezultat:** plan zostaje zapisany, a na liście trenera przy osobie widać „Plan:
<nazwa>”. Członek widzi plan z właściwą kolejnością i parametrami (`MBR-05`).

### PLAN-02 · Edycja planu — P1
**Kroki:** otwórz plan członka, który już go ma; usuń jedno ćwiczenie, dodaj inne, zmień parametry →
„Zapisz zmiany”.
**Oczekiwany rezultat:** członek po odświeżeniu widzi zmieniony plan. Członek zawsze ma najwyżej
jeden aktywny plan.

### PLAN-03 · Walidacja planu — P2
**Kroki:** zapisz: bez nazwy; bez ćwiczeń; z seriami 0 lub 21; z ciężarem 1000; z przerwą 3601 s; z
notatką dłuższą niż 500 znaków.
**Oczekiwany rezultat:** „Podaj nazwę planu.”, „Dodaj przynajmniej jedno ćwiczenie do planu.”,
komunikat przy wierszu „Sprawdź wartości w tym wierszu: serie 1–20, ciężar 0–999.99 kg, przerwa
0–3600 s.”; plan się nie zapisuje.

### PLAN-04 · Ograniczenia biblioteki w kreatorze — P3
**Oczekiwany rezultat:** ćwiczenie już dodane znika z listy do wyboru (nie da się dodać dwa razy).
Przy wyszukiwaniu bez wyników widać „Żadne ćwiczenie nie pasuje do „…”.”. Po 50 ćwiczeniach
pojawia się „Plan ma już 50 ćwiczeń — to maksimum…”.

### PLAN-05 · Plan dla osoby bez konta — P2
**Oczekiwany rezultat:** kreator pokazuje „Bez konta — ta osoba zobaczy plan w aplikacji dopiero,
gdy założy konto.”; plan się zapisuje. Po rejestracji z kodem (`REG-01`) osoba widzi ten plan.

### PLAN-06 · Plan nie dla obsługi — P2
**Kroki:** wpisz ręcznie `/admin/members/<id trenera>/plan` i spróbuj przypisać plan.
**Oczekiwany rezultat:** „Planu treningowego nie przypisuje się trenerom ani administratorom.”
(albo brak możliwości otwarcia kreatora).

### PLAN-07 · Równoczesna edycja tego samego planu — P3
**Kroki:** otwórz ten sam plan w dwóch kartach, zapisz zmianę w pierwszej, potem inną w drugiej.
**Oczekiwany rezultat:** druga karta dostaje „Ktoś zmieniał ten plan w tej samej chwili. Odśwież
stronę i spróbuj ponownie.”; zmiany z pierwszej nie zostają po cichu nadpisane.

### PLAN-08 · Lista członków trenera — P2
**Rola:** trener
**Oczekiwany rezultat:** widać tylko aktywnych członków (bez obsługi i zablokowanych), z informacją
„Bez konta ·” i „Plan: …” / „Brak planu”; wyszukiwanie po imieniu i nazwisku; stronicowanie
„Poprzednia” / „Następna”.
