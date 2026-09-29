# Środowisko i dane testowe

- **Adres:** https://po-prostu-silka.azurewebsites.net (jedyne środowisko, tryb `Staging`).
- **Dane:** środowisko jest wypełnione deterministycznym „klubem testowym”. Wszystkie konta testowe
  mają **to samo hasło**, które przekaże właściciel projektu (nie ma go w repozytorium).

| Konto | Persona | Uwagi |
| --- | --- | --- |
| `admin1@example.test` | Administrator | nie prowadzi żadnych zajęć |
| `admin2@example.test` | Administrator (+ rola Trenera) | prowadzi część zajęć, więc widać na nim pierwszeństwo Admin > Trener |
| `trener1@example.test`, `trener2@example.test` | Trener | prowadzą zajęcia |
| `czlonek001@example.test` … `czlonek160@example.test` | Członek | 4 z nich są zablokowane (znajdziesz je filtrem „Zablokowani”) |
| `bezkonta01` … `bezkonta40` | kartoteki **bez konta** | co druga ma aktywny kod zaproszenia; część nie ma e-maila; jedna jest zablokowana |

Dane zawierają m.in. karnety ważne, wygasłe i wykorzystane (6 osób ma karnet bez wolnych wejść),
zajęcia z 4 tygodni wstecz i 4 do przodu, 3 pełne zajęcia (od jutra), zajęcia odwołane, 8 aktywnych
planów i ok. 27 ćwiczeń.

**Ważne ograniczenia środowiska:**

1. Adresy `@example.test` **nie odbierają poczty**. Do testów e-maili, resetu hasła i powiadomień
   push załóż konto na **własny adres** tak, jak w `REG-01` (administrator tworzy kartotekę,
   generuje kod, a ty rejestrujesz się z linku). To samo w sobie jest przypadkiem testowym.
2. Środowisko może zostać **zresetowane** (ponowne wgranie danych testowych). Wtedy znikają
   wszystkie zmiany, także konta założone na prawdziwe adresy. Przed dłuższą sesją testów zapytaj,
   czy reset jest planowany.
3. Środowisko jest jedno, więc to, co zmienisz, widzą wszyscy. Twórz dane z rozpoznawalnym sufiksem
   (np. `Test-QA 2026-09-28 14:05`), żeby było wiadomo, co jest twoje, i nie psuj danych, na których
   opierają się inne przypadki (np. nie blokuj `trener1`).
4. Strefa czasowa klubu to czas polski. Godziny zajęć i daty ważności karnetów liczone są w czasie
   klubu.

## Urządzenia i szerokości ekranu

Aplikacja zmienia układ na kilku progach szerokości, więc część TC trzeba wykonać na konkretnej:

| Szerokość | Co się zmienia |
| --- | --- |
| **≤ 480 px** (telefon) | dolny pasek nawigacji zamiast menu w nagłówku; przypięty górny pasek z tytułem ekranu i strzałką „wstecz” |
| **≥ 640 px** | formularze układają pola w kolumny; lista członków jako tabela |
| **≥ 768 px** | kalendarz pokazuje cały tydzień zamiast jednego dnia |
| **≥ 1024 px** (komputer) | administrator dostaje kalendarz do zarządzania zajęciami (przeciąganie, zmiana rozmiaru); poniżej tej szerokości ten ekran jest świadomie niedostępny |

Minimalny zestaw: **Chrome na komputerze**, **Chrome na Androidzie**, **Safari na iPhonie**
(iPhone pokazuje push tylko po dodaniu aplikacji do ekranu początkowego). Dodatkowo, jeśli czas
pozwoli: Firefox i Edge na komputerze.
