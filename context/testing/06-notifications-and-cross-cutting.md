# Powiadomienia, PWA i zachowania przekrojowe

Konwencje (priorytety, statusy, format przypadku) i dane testowe: [README](README.md), [środowisko i dane testowe](environment.md).

## Powiadomienia i PWA (NOTIF, PWA)

> Wymaga konta członka na prawdziwym adresie e-mail (z `REG-01`), a do push — przeglądarki, która
> wspiera powiadomienia (na iPhonie tylko po dodaniu do ekranu początkowego).

### NOTIF-01 · E-mail i push po odwołaniu zajęć — P1
**Kroki:**
1. Jako członek (telefon) przy komunikacie „Chcesz wiedzieć od razu, gdy zajęcia się zmienią?”
   kliknij „Włącz” i zezwól na powiadomienia.
2. Administrator zapisuje tego członka na przyszłe zajęcia i odwołuje je (`CLS-10`).
3. Zanotuj godzinę odwołania i godziny otrzymania e-maila i push.

**Oczekiwany rezultat:** w ciągu kilku minut przychodzi **e-mail** i **powiadomienie push** z nazwą i
terminem zajęć. Zajęcia znikają z „Nadchodzące” członka i pojawiają się w historii jako odwołane.
Opóźnienie powyżej 5 minut zgłoś jako błąd.

### NOTIF-02 · Powiadomienie o zmianie zajęć — P1
**Kroki:** administrator zmienia godzinę zajęć, na które zapisany jest członek z `NOTIF-01`.
**Oczekiwany rezultat:** e-mail i push informujące o zmianie z nowym terminem.

### NOTIF-03 · „Nie teraz” przy prośbie o powiadomienia — P3
**Oczekiwany rezultat:** komunikat znika i nie wraca natychmiast po odświeżeniu. Aplikacja działa
dalej normalnie (e-maile nadal przychodzą).

### NOTIF-04 · Osoba bez konta nie dostaje powiadomień — P3
**Oczekiwany rezultat:** odwołanie zajęć z zapisaną osobą bez konta przebiega bez błędu (nie ma
dokąd wysłać).

### PWA-01 · Instalacja na Androidzie / komputerze — P2
**Kroki:** przy komunikacie „Dodać Po Prostu Siłkę do ekranu głównego?” kliknij „Zainstaluj”.
**Oczekiwany rezultat:** aplikacja instaluje się i otwiera jako osobne okno bez paska przeglądarki,
z ikoną i nazwą.

### PWA-02 · iPhone — instrukcja ręczna — P2
**Kroki:** w Safari na iPhonie poczekaj na komunikat instalacji.
**Oczekiwany rezultat:** instrukcja „Stuknij Udostępnij… potem Do ekranu początkowego”. Po dodaniu
aplikacja otwiera się na pełnym ekranie, a `NOTIF-01` działa także na iPhonie.

---

## Zachowania przekrojowe (X)

### X-01 · Brak internetu w trakcie akcji — P2
**Kroki:** w trakcie wypełniania formularza (np. karnet, plan) lub przed kliknięciem akcji w wierszu
wyłącz sieć i wyślij.
**Oczekiwany rezultat:** „Brak połączenia z serwerem. Sprawdź internet i spróbuj ponownie.”. Wpisane
dane zostają w formularzu; po włączeniu sieci ponowne wysłanie działa.

### X-02 · Komunikaty „toast” — P3
**Oczekiwany rezultat:** komunikaty sukcesu i informacyjne znikają same po kilku sekundach, a
komunikaty błędu zostają, dopóki ich nie zamkniesz. Nie zasłaniają dolnego paska ani przycisków
akcji na telefonie.

### X-03 · Nieaktualne dane w dwóch kartach — P3
**Kroki:** w karcie A otwórz listę członków; w karcie B zmień dane lub zablokuj osobę; w A wykonaj
akcję na tej osobie.
**Oczekiwany rezultat:** czytelny komunikat, np. „Ktoś właśnie zmienił dane tej osoby. Odśwież listę
i spróbuj ponownie.” albo „Lista była nieaktualna — odświeżono.”, bez błędu technicznego.

### X-04 · Dostępność — klawiatura — P3
**Kroki:** przejdź logowanie, nawigację, formularz karnetu i nakładkę zapisanych wyłącznie
klawiaturą (Tab, Shift+Tab, Enter, spacja, strzałki w menu wiersza).
**Oczekiwany rezultat:** fokus jest zawsze widoczny; wszystkie akcje są osiągalne; po otwarciu
nakładki fokus jest w niej, a po zamknięciu wraca do elementu, który ją otworzył.

### X-05 · Wygląd i spójność — P3
**Oczekiwany rezultat:** jedna ikona na jedno znaczenie na wszystkich ekranach; pola tekstowe i
listy rozwijane wyglądają tak samo; listy rozwijane mają strzałkę; na telefonie nic nie wychodzi
poza ekran w poziomie i nie wymaga przewijania na boki (poza kalendarzem zarządzania, który na
telefonie jest wyłączony). Polskie znaki wyświetlają się poprawnie.

### X-06 · Tryb ciemny systemu i powiększenie tekstu — P3
**Kroki:** włącz tryb ciemny w systemie; ustaw w przeglądarce powiększenie 200%.
**Oczekiwany rezultat:** tekst pozostaje czytelny, a układ się nie rozsypuje. Każdą rozbieżność
opisz ze zrzutem ekranu.

### X-07 · Brak wycieku szczegółów technicznych — P3
**Oczekiwany rezultat:** w żadnym komunikacie widocznym dla użytkownika nie ma śladów stosu,
nazw klas, zapytań SQL ani angielskich komunikatów technicznych.
