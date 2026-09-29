# Członkowie i karnety (administrator)

Konwencje (priorytety, statusy, format przypadku) i dane testowe: [README](README.md), [środowisko i dane testowe](environment.md).

## Członkowie — ekran administratora (MEM)

### MEM-01 · Lista członków — widok i stronicowanie — P1
**Rola:** administrator
**Kroki:** otwórz „Członkowie” na komputerze, potem na telefonie.
**Oczekiwany rezultat:**
- Na komputerze tabela z kolumnami Członek, Status, Rola, Karnet, W klubie od i akcje; na telefonie
  jeden zwarty wiersz na osobę.
- Widać licznik osób i nawigację stron „1–N z 204” (strzałki), a przejście na kolejną stronę
  pokazuje kolejne osoby.
- Kolumna karnetu rozróżnia: ważny (z liczbą wejść), wykorzystany (0 wejść) i brak.

### MEM-02 · Wyszukiwanie — P1
**Kroki:** wpisz w „Szukaj” kolejno: fragment nazwiska, fragment e-maila (`czlonek01`), nazwisko z
polskimi znakami wpisane bez nich (np. `lukasz` dla „Łukasz”, `zolw` dla „Żółw”), wielkimi literami.
**Oczekiwany rezultat:** wyniki pojawiają się po krótkiej pauzie w pisaniu, nie po każdym znaku.
Wielkość liter i polskie znaki (w tym „ł”) nie mają znaczenia. Gdy nic nie pasuje: „Brak członków
pasujących do wyszukiwania.”

### MEM-03 · Filtry statusu i roli — P2
**Kroki:** wybierz Status „Aktywni”, „Zablokowani”, „Bez konta”; potem Rola „Trenerzy”,
„Administratorzy”; połącz filtr z wyszukiwaniem; kliknij „Wyczyść filtry”.
**Oczekiwany rezultat:** lista i licznik odpowiadają filtrom; „Wyczyść filtry” przywraca pełną
listę. „Zablokowani” pokazuje 5 osób z danych testowych (4 z kontem i 1 bez).

### MEM-04 · Dodanie kartoteki bez konta — P1
**Kroki:** „Dodaj członka” → wpisz imię i nazwisko → „Zapisz”.
**Oczekiwany rezultat:** osoba pojawia się na liście ze statusem „Bez konta”. Formularz wyjaśnia, że
nie tworzy się tu loginu i nie podaje e-maila.

### MEM-05 · Walidacja kartoteki — P2
**Kroki:**
1. Zapisz bez imienia i nazwiska; potem z nazwą dłuższą niż 100 znaków.
2. Wypełnij tylko „Ulica” (reszta adresu pusta) i zapisz.
3. Wpisz błędny telefon i kod pocztowy.

**Oczekiwany rezultat:**
1. „Podaj imię i nazwisko (maksymalnie 100 znaków).”
2. Podpowiedź „Zacząłeś wpisywać adres — uzupełnij wszystkie pola albo wyczyść je.” i brak zapisu.
3. Komunikaty o formacie jak w `MBR-10`.

### MEM-06 · Edycja danych członka z kontem — P2
**Kroki:** menu wiersza → „Edytuj dane” u członka z kontem; zmień telefon i zapisz.
**Oczekiwany rezultat:** adnotacja, że e-mail służy do logowania i nie zmienia się tutaj; zmiana
jest widoczna w „Moje konto” tego członka (`MBR-09`).

### MEM-07 · Kod klubowicza: pokaż, kopiuj, zamknij — P2
**Kroki:** u osoby bez konta z aktywnym kodem wybierz „Pokaż kod klubowicza”, potem „Kopiuj kod”,
„Kopiuj link”, „Zamknij”.
**Oczekiwany rezultat:** widać kod i „Ważny do …”; po skopiowaniu pojawia się „Skopiowano.”, a w
schowku jest odpowiednio kod albo pełny link `…/register?invitationCode=…`.

### MEM-08 · Kod nie dla osoby z kontem ani zablokowanej — P2
**Oczekiwany rezultat:** w menu osoby z kontem nie ma opcji kodu. U zablokowanej osoby bez konta
wygenerowanie kodu jest niemożliwe albo kończy się komunikatem „Ta osoba jest zablokowana —
odblokuj ją, zanim wydasz kod.”.

### MEM-09 · Nadanie i odebranie roli Trenera — P1
**Kroki:**
1. U aktywnego członka z kontem (bez karnetu, zapisów i planu, np. świeżego z `REG-01`) wybierz
   „Nadaj rolę Trenera”.
2. Zaloguj się na to konto i sprawdź menu.
3. Wybierz „Odbierz rolę Trenera”, zaloguj się ponownie.

**Oczekiwany rezultat:** w kroku 2 konto ma menu trenera, a na liście administratora wiersz
pokazuje rolę Trenera i nie ma już opcji „Karnety” / „Plan”. W kroku 3 konto wraca do menu członka.

### MEM-10 · Rola Trenera wymaga konta — P2
**Oczekiwany rezultat:** u osoby bez konta opcji „Nadaj rolę Trenera” nie ma albo próba kończy się
„Ta osoba nie ma konta — rola Trenera wymaga logowania.”.

### MEM-11 · Blokowanie i odblokowanie — P1
**Warunki wstępne:** członek z karnetem i co najmniej jednym przyszłym zapisem (utwórz go sam).
**Kroki:**
1. Menu → „Zablokuj”.
2. Sprawdź listę zapisanych na te przyszłe zajęcia (`BOOK-02`).
3. Spróbuj zalogować się jako ta osoba (jeśli ma konto).
4. Menu → „Odblokuj”.

**Oczekiwany rezultat:** status zmienia się na zablokowany; przyszłe zapisy tej osoby zostają
zwolnione, więc miejsca wracają; logowanie jest odrzucone jak w `AUTH-05`. Po odblokowaniu osoba
może się zalogować, ale zwolnione zapisy nie wracają same.

### MEM-12 · Administratora nie da się zablokować — P2
**Oczekiwany rezultat:** w menu wiersza administratora nie ma opcji „Zablokuj”.

---

## Karnety (PASS)

### PASS-01 · Wystawienie karnetu — P1
**Rola:** administrator
**Kroki:** członek bez aktywnego karnetu → menu → „Karnety”. Wpisz nazwę (np. „Karnet 8 wejść”),
„Ważny od” dziś, „Ważny do” za miesiąc, 8 wejść → „Wystaw karnet”.
**Oczekiwany rezultat:** nad formularzem pojawia się „Aktywny karnet: … — zostało 8 z 8 wejść,
ważny do …”. Karnet jest w „Historii karnetów” oznaczony jako bieżący, a członek widzi go na
Starcie (`DASH-01`).

### PASS-02 · Walidacja karnetu — P2
**Kroki:** spróbuj zapisać: bez nazwy; „Ważny do” wcześniejszy niż „Ważny od”; 0 wejść; 501 wejść.
**Oczekiwany rezultat:** odpowiednio „Podaj nazwę karnetu (maksymalnie 100 znaków).”,
„Nieprawidłowy zakres dat — data końca nie może być wcześniejsza niż data startu.”, „Podaj liczbę
wejść od 1 do 500.”.

### PASS-03 · Karnety nie mogą się nakładać — P1
**Kroki:** osobie z aktywnym karnetem wystaw drugi, którego zakres dat częściowo pokrywa się z
pierwszym.
**Oczekiwany rezultat:** odmowa „Ta osoba ma już karnet obejmujący część tego okresu. Karnety nie
mogą się nakładać.”. Karnet zaczynający się dzień po końcu poprzedniego zostaje przyjęty.

### PASS-04 · Obie daty są włącznie — P2
**Kroki:** wystaw karnet ważny do dziś i zapisz osobę na zajęcia dziś wieczorem; wystaw inny,
ważny od jutra, i spróbuj zapisać na zajęcia dziś.
**Oczekiwany rezultat:** pierwszy zapis się udaje (dzień końca jest wliczony); drugi odmowa „Ta
osoba nie ma karnetu ważnego w dniu tych zajęć.”.

### PASS-05 · Edycja karnetu — P2
**Kroki:** „Edytuj” przy karnecie z wykorzystanymi wejściami; zmień nazwę i datę końca, zapisz. Potem
ustaw liczbę wejść mniejszą niż już wykorzystana.
**Oczekiwany rezultat:** pierwsza zmiana się zapisuje. Druga daje odmowę „Nieprawidłowa liczba
wejść… nie może mieć ich mniej, niż już wykorzystano.”. „Anuluj edycję” wraca do trybu
wystawiania.

### PASS-06 · Usunięcie karnetu — P2
**Kroki:** spróbuj usunąć karnet, z którego opłacono aktywne (przyszłe) zapisy; potem karnet bez
zapisów.
**Oczekiwany rezultat:** pierwszy: „Z tego karnetu opłacono aktywne zapisy. Najpierw wypisz osobę z
tych zajęć, potem usuń karnet.”. Drugi znika z historii.

### PASS-07 · Karnet dla zablokowanego i dla obsługi — P2
**Kroki:** otwórz „Karnety” zablokowanej osoby. Następnie wpisz ręcznie adres
`/admin/members/<id trenera>/passes` (id skopiuj z linku „Edytuj dane” trenera) i spróbuj wystawić
karnet.
**Oczekiwany rezultat:** „Ta osoba jest zablokowana — najpierw ją odblokuj, potem wystaw karnet.”
oraz „Karnetu nie wystawia się trenerom ani administratorom.”.

### PASS-08 · Pozostałe wejścia liczą się z zapisów i obecności — P1
**Kroki:** osobie z karnetem 8/8 zrób 2 zapisy (`BOOK-01`); sprawdź karnet. Wypisz ją z jednego
(`BOOK-04`); sprawdź karnet. Po zajęciach oznacz ją jako nieobecną (`ATT-02`) i sprawdź ponownie.
**Oczekiwany rezultat:** po dwóch zapisach zostaje 6; po wypisaniu 7; oznaczenie nieobecności
zwraca wejście. Stan jest spójny na Starcie członka, w „Karnety” i w kolumnie „Karnet” na liście.
