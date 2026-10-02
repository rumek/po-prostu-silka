# Ekrany członka: Start, zajęcia, plan, profil

Konwencje (priorytety, statusy, format przypadku) i dane testowe: [README](README.md), [środowisko i dane testowe](environment.md).

## Ekran Start (DASH)

### DASH-01 · Start członka z karnetem i zapisami — P1
**Rola:** członek z ważnym karnetem i przyszłymi zapisami (znajdź go na liście członków po kolumnie
„Karnet”).
**Oczekiwany rezultat:**
- Karta „Najbliższe zajęcia” pokazuje najbliższy zapis: nazwę, dzień i godzinę, czas trwania,
  prowadzącego. Przy większej liczbie zapisów jest link „Zobacz wszystkie” do „Moje zajęcia”.
- Karta „Twój karnet” pokazuje nazwę karnetu, liczbę „Pozostałe wejścia” w formie „X z Y”, datę
  „ważny do …” i wizualizację wejść (kratki przy małej liczbie wejść, pasek przy dużej, np. OPEN 30).
- Na karcie „Twój karnet” jest słowo „Opłacony” albo znacznik „Nieopłacony” — zgodnie z tym, co
  administrator widzi przy bieżącym karnecie tej osoby (`PASS-09`, `PASS-11`). Dotyczy tylko
  dzisiejszego karnetu: wcześniejszy nieopłacony karnet nie jest tu pokazany.
- Liczby zgadzają się z tym, co administrator widzi w „Karnety” tej osoby.

### DASH-02 · Start członka bez karnetu i bez zapisów — P2
**Kroki:** zaloguj się jako członek bez karnetu (lub nowa osoba z `REG-01` przed wystawieniem
karnetu).
**Oczekiwany rezultat:** „Nie masz jeszcze żadnych zapisów. Zapisy prowadzi klub — odezwij się w
recepcji.” oraz „Nie masz aktywnego karnetu. Bez niego klub nie zapisze Cię na zajęcia — …”.

### DASH-03 · Start trenera — P1
**Rola:** `trener1`
**Oczekiwany rezultat:** sekcja „Twoje zajęcia” z kartami „Dzisiaj” i „Nadchodzące”. Każde zajęcia
mają liczbę zapisanych w formie „zapisani/miejsca”, a pod spodem jest link „Zobacz grafik”. Na
ekranie nie ma kart karnetu ani „Najbliższe zajęcia”.

### DASH-04 · Błąd wczytania karty i ponowienie — P3
**Kroki:** na Starcie wyłącz sieć (tryb samolotowy / DevTools „Offline”) i odśwież kartę albo
przejdź do Startu z innego ekranu; potem włącz sieć i kliknij „Spróbuj ponownie”.
**Oczekiwany rezultat:** przy karcie pojawia się komunikat „Nie udało się wczytać …” z przyciskiem
„Spróbuj ponownie”, który po przywróceniu sieci wczytuje dane.

---

## Ekrany członka: zajęcia, historia, plan, profil (MBR)

### MBR-01 · Moje zajęcia — nadchodzące — P1
**Rola:** członek z przyszłymi zapisami
**Kroki:** otwórz „Zajęcia”.
**Oczekiwany rezultat:** nagłówek „Moje zajęcia” z panelem „Najbliższe: …”; zakładka „Nadchodzące”
jest aktywna; zapisy są pogrupowane po miesiącach, posortowane od najbliższego. Na ekranie nie ma
przycisku zapisu ani wypisania (członek nie zapisuje się sam).

### MBR-02 · Moje zajęcia — historia obecności — P1
**Kroki:** przełącz na zakładkę „Historia”.
**Oczekiwany rezultat:**
- Przeszłe zajęcia są pogrupowane po miesiącach, a przy miesiącu widać podsumowanie „X/Y
  obecności”.
- Każde zajęcia mają oznaczenie: obecny / nieobecny (ikona i słowo). Zajęcia odwołane są
  przekreślone z adnotacją. Zajęcia bez oznaczenia obecności nie mają ikony.
- Na dole jest przycisk „Pokaż wcześniejsze”, który dociąga starsze miesiące, jeśli istnieją.

### MBR-03 · Przełączanie zakładek gestem i klawiaturą — P3
**Kroki:** na telefonie przesuń palcem w lewo i w prawo po liście. Na komputerze przejdź tabulatorem
do zakładek i użyj strzałek.
**Oczekiwany rezultat:** zakładki przełączają się z animacją; strzałki zmieniają zakładkę.

### MBR-04 · Brak zapisów i pusta historia — P3
**Rola:** świeże konto z `REG-01` bez zapisów
**Oczekiwany rezultat:** „Nie masz jeszcze żadnych zapisów…” oraz w historii „Nie masz jeszcze
zajęć w historii.”.

### MBR-05 · Mój plan — karta ćwiczeń — P1
**Rola:** członek z przypisanym planem (na liście trenera „Członkowie” widać „Plan: …”).
**Kroki:** otwórz „Plan”.
**Oczekiwany rezultat:**
- Nagłówek z nazwą planu, kto go przypisał i kiedy.
- Każde ćwiczenie ma pozycję „n / wszystkich”, nazwę, grupę mięśniową (etykieta), parametry (serie,
  powtórzenia, ciężar, czas, przerwa — tylko te, które trener podał) i notatkę trenera wyróżnioną
  ikoną „i”.
- Ikony parametrów są takie same jak w kreatorze planu u trenera.

### MBR-06 · Szczegóły ćwiczenia z planu i film — P1
**Kroki:** w planie kliknij ikonę „i” przy ćwiczeniu, które ma film.
**Oczekiwany rezultat:** otwiera się ekran z nazwą, opisem, instrukcjami (przygotowanie, pozycja
wyjściowa, wykonanie) i odtwarzaczem YouTube, który odtwarza film. „Wróć do planu” wraca do planu.
Ćwiczenie bez opisu i filmu pokazuje „Do tego ćwiczenia nie dodano jeszcze opisu ani filmu…”.

### MBR-07 · Ćwiczenie spoza planu przez adres — P3
**Kroki:** zmień w adresie `/my-plan/exercises/<id>` identyfikator na ćwiczenie, którego nie ma w
planie (np. skopiowany z ekranu administratora).
**Oczekiwany rezultat:** „Tego ćwiczenia nie ma w Twoim planie.” z linkiem „Wróć do planu”.

### MBR-08 · Brak planu — P3
**Oczekiwany rezultat:** „Nie masz jeszcze przypisanego planu treningowego. Poproś trenera, żeby Ci
go ułożył.”

### MBR-09 · Moje konto — dane kontaktowe — P1
**Rola:** dowolna
**Kroki:** Więcej → Moje konto. Zmień numer telefonu i adres, kliknij „Zapisz zmiany”, odśwież
stronę.
**Oczekiwany rezultat:** imię, nazwisko i e-mail są tylko do odczytu, z adnotacją, że prowadzi je
obsługa. Po zapisie widać potwierdzenie, a po odświeżeniu zmiany są zachowane. Administrator widzi
nowe dane w edycji tego członka.

### MBR-10 · Moje konto — walidacja danych kontaktowych — P2
**Kroki:** wpisz telefon `12345`, kod pocztowy `00000`, zostaw puste „Ulica” i „Miejscowość”,
zapisz.
**Oczekiwany rezultat:** komunikaty przy polach: „Podaj numer telefonu w formacie 123 456 789.”,
„Podaj kod pocztowy w formacie 00-000.”, „Podaj nazwę ulicy.”, „Podaj miejscowość.”; dane się nie
zapisują.

### MBR-11 · Moje konto — brak danych kontaktowych — P3
**Warunki wstępne:** konto z `REG-01` założone na kartotece bez danych adresowych.
**Oczekiwany rezultat:** nad formularzem widać „Uzupełnij swoje dane kontaktowe — potrzebujemy ich,
żeby móc się z Tobą skontaktować.”.
