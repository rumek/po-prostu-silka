# Gym Management App — Ogólny zarys projektu

## Cel projektu

Aplikacja webowa dla siłowni / klubu fitness, która łączy w jednym miejscu obsługę użytkowników, grafik zajęć, zapisy na zajęcia oraz indywidualne plany treningowe.

Aplikacja ma działać wygodnie przede wszystkim na telefonie i być dostępna również jako PWA.

## Główne obszary aplikacji

### 1. Użytkownik

Użytkownik może:

- rejestrować konto i logować się,
- przeglądać grafik zajęć,
- zapisywać się na zajęcia,
- anulować swoje zapisy,
- przeglądać swoje nadchodzące zajęcia,
- otrzymywać informacje o zmianach i odwołaniach,
- zarządzać swoim profilem,
- korzystać ze swojego indywidualnego planu treningowego,
- przeglądać bibliotekę ćwiczeń,
- poznawać sposób wykonywania ćwiczeń,
- oglądać materiały wideo dotyczące ćwiczeń.

### 2. Grafik zajęć

Jednym z głównych elementów aplikacji jest grafik zajęć grupowych.

Użytkownik może zobaczyć informacje o zajęciach, takie jak:

- nazwa,
- data i godzina,
- sala / lokalizacja,
- prowadzący,
- liczba dostępnych miejsc.

Z tego miejsca może zapisać się na zajęcia.

Administrator zarządza całym grafikiem — tworzy zajęcia, zmienia ich informacje, powiela je na kolejne tygodnie oraz może je odwoływać.

### 3. Zapisy na zajęcia

Aplikacja pełni funkcję systemu rezerwacji miejsc na zajęcia.

Użytkownik może zarezerwować miejsce, jeżeli zajęcia są dostępne. Po anulowaniu zapisu informacja o wcześniejszej rezerwacji pozostaje w historii.

W MVP nie ma listy rezerwowej.

### 4. Indywidualne plany treningowe

Administrator może przygotować użytkownikowi indywidualny plan treningowy.

Plan zawiera uporządkowaną listę ćwiczeń wraz z parametrami potrzebnymi do wykonania treningu, np.:

- liczbą serii,
- liczbą powtórzeń,
- ciężarem,
- czasem odpoczynku,
- notatką.

Użytkownik ma łatwy dostęp do swojego aktualnego planu po zalogowaniu.

### 5. Biblioteka ćwiczeń

Aplikacja zawiera bibliotekę ćwiczeń.

Każde ćwiczenie może zawierać:

- opis,
- grupę mięśniową,
- poziom trudności,
- wymagany sprzęt,
- instrukcję przygotowania,
- opis pozycji początkowej,
- instrukcję wykonania,
- film instruktażowy.

Dzięki temu plan treningowy nie jest tylko listą nazw ćwiczeń — użytkownik może sprawdzić, jak poprawnie wykonać dane ćwiczenie.

Filmy instruktażowe są materiałami z YouTube.

### 6. Powiadomienia

Powiadomienia są wspólnym elementem całego systemu.

Użytkownik może zostać poinformowany m.in. o:

- zaakceptowaniu konta,
- odrzuceniu konta,
- odwołaniu zajęć,
- zmianie zajęć,
- innych komunikatach.

Powiadomienia są dostępne bezpośrednio w aplikacji, a dodatkowo mogą być wysyłane emailem lub jako powiadomienia push.

### 7. Administrator

Administrator odpowiada za obsługę całego klubu.

#### Użytkownicy

- przeglądanie użytkowników,
- obsługa oczekujących rejestracji,
- akceptowanie użytkowników,
- odrzucanie użytkowników,
- blokowanie i odblokowywanie użytkowników.

#### Grafik

- tworzenie zajęć,
- edycja zajęć,
- powielanie zajęć,
- odwoływanie zajęć,
- przeglądanie zapisów.

#### Trening

- tworzenie planów treningowych,
- przypisywanie planów użytkownikom,
- zarządzanie ćwiczeniami,
- zarządzanie instrukcjami,
- przypisywanie filmów do ćwiczeń.

## Role

Projekt posiada obecnie dwie role:

- **User** — korzysta z aplikacji.
- **Admin** — zarządza aplikacją, użytkownikami, grafikiem i treningami.

Osobna rola Trener nie jest częścią MVP.

## Status użytkownika

Proces użytkownika można uprościć do:

**Rejestracja → oczekiwanie na akceptację → aktywne konto → korzystanie z aplikacji**

Administrator może również odrzucić lub zablokować konto.

## Główne ekrany użytkownika

```text
LOGOWANIE / REJESTRACJA
        ↓
    DASHBOARD
     ├── Grafik
     │    └── Szczegóły zajęć
     │         └── Zapis
     │
     ├── Plan treningowy
     │    └── Ćwiczenia
     │         ├── Instrukcja
     │         └── Film
     │
     ├── Powiadomienia
     │
     └── Profil
```

Dashboard użytkownika powinien przede wszystkim pokazywać:

- najbliższe zajęcia,
- najbliższy / aktywny trening,
- najważniejsze nieprzeczytane powiadomienia.

## Panel administratora

```text
ADMIN DASHBOARD
     ├── Użytkownicy
     │    ├── Oczekujący
     │    ├── Aktywni
     │    ├── Zablokowani
     │    └── Odrzuceni
     │
     ├── Grafik
     │    ├── Zajęcia
     │    └── Zapisy
     │
     ├── Plany treningowe
     │
     ├── Ćwiczenia
     │
     └── Powiadomienia
```

Dashboard administratora ma przede wszystkim pokazywać rzeczy wymagające uwagi, np. liczbę osób oczekujących na akceptację oraz dzisiejsze i nadchodzące zajęcia.

## Charakter aplikacji

Projekt ma być prostym i dobrze zaprojektowanym MVP, a nie rozbudowaną platformą enterprise.

Najważniejsze elementy produktu to:

### 1. Zajęcia

> Co dzisiaj / w tym tygodniu dzieje się w klubie i na co mogę się zapisać?

### 2. Trening

> Jaki mam trening i jak mam wykonać poszczególne ćwiczenia?

### 3. Obsługa użytkownika

> Mam konto, moje zapisy, moje powiadomienia i mój profil.

Nad tym wszystkim znajduje się panel administratora, który zarządza użytkownikami, grafikiem i treningami.

## Zakres MVP

MVP obejmuje:

- rejestrację i logowanie,
- obsługę użytkowników i akceptację kont,
- grafik zajęć,
- zapisy i anulowanie zapisów,
- zarządzanie zajęciami przez administratora,
- indywidualne plany treningowe,
- bibliotekę ćwiczeń,
- instrukcje i filmy ćwiczeń,
- powiadomienia,
- responsywną aplikację PWA.

## Poza MVP

Na późniejszy etap pozostają m.in.:

- sprzedaż karnetów,
- płatności,
- automatyczne subskrypcje,
- faktury,
- lista rezerwowa,
- pełna obsługa cyklicznych serii zajęć,
- rola Trener,
- czaty i funkcje społecznościowe,
- integracje z Apple Health / Google Fit,
- natywne aplikacje mobilne,
- własne przechowywanie filmów,
- zaawansowane statystyki treningowe,
- automatyczne progresowanie ciężaru.

## Docelowa idea

Aplikacja ma zapewnić użytkownikowi prosty model:

**Zaloguj się → zobacz swoje zajęcia → zarezerwuj → sprawdź swój trening → wykonaj go korzystając z instrukcji → odbieraj ważne informacje z klubu.**
