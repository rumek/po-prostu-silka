# Konto i dostęp: logowanie, rejestracja, hasło, nawigacja

Konwencje (priorytety, statusy, format przypadku) i dane testowe: [README](README.md), [środowisko i dane testowe](environment.md).

## Logowanie, sesja i wylogowanie (AUTH)

### AUTH-01 · Logowanie poprawnymi danymi (każda persona) — P1
**Rola:** członek, trener, administrator
**Warunki wstępne:** wylogowany; znane hasło kont testowych.
**Kroki:**
1. Otwórz aplikację. Powinien pokazać się ekran „Zaloguj się”.
2. Wpisz `czlonek010@example.test` i hasło, kliknij „Zaloguj się”.
3. Powtórz dla `trener1@example.test` i `admin1@example.test`.

**Oczekiwany rezultat:**
- W trakcie wysyłania przycisk pokazuje „Logowanie…” i jest nieaktywny.
- Po zalogowaniu widać ekran Start z powitaniem „Cześć, <imię i nazwisko>!”.
- Menu odpowiada personie (zob. `NAV-01`).

### AUTH-02 · Błędne hasło i nieistniejący e-mail dają ten sam komunikat — P1
**Rola:** wylogowany
**Kroki:**
1. Zaloguj się istniejącym e-mailem (`czlonek010@example.test`) i złym hasłem.
2. Zaloguj się nieistniejącym e-mailem (`nie-ma-mnie@example.test`) i dowolnym hasłem.

**Oczekiwany rezultat:**
- W obu przypadkach nad formularzem pojawia się ten sam komunikat „Nieprawidłowy e-mail lub
  hasło.”, a nie błąd przy konkretnym polu.
- Nic nie zdradza, czy konto o tym adresie istnieje.

### AUTH-03 · Walidacja pustych i błędnych pól logowania — P2
**Kroki:**
1. Kliknij w pole e-mail, potem poza nie, bez wpisywania. Tak samo z hasłem.
2. Wpisz `abc` jako e-mail i przejdź dalej.
3. Kliknij „Zaloguj się” z pustymi polami.

**Oczekiwany rezultat:** pod polami widać „Podaj poprawny adres e-mail.” i „Podaj hasło.”; żadne
żądanie logowania nie zostaje wykonane.

### AUTH-04 · Pokazywanie hasła — P3
**Kroki:** wpisz hasło i kliknij ikonę oka w polu hasła, potem kliknij ją jeszcze raz.
**Oczekiwany rezultat:** hasło staje się widoczne, a ikona zmienia się na przekreślone oko; drugie
kliknięcie znowu je ukrywa.

### AUTH-05 · Zablokowane konto nie może się zalogować — P1
**Warunki wstępne:** administrator znalazł zablokowanego członka z kontem (Członkowie → Status:
„Zablokowani”).
**Kroki:** zaloguj się e-mailem tego członka i poprawnym hasłem.
**Oczekiwany rezultat:** komunikat „Twoje konto zostało zablokowane. Skontaktuj się z obsługą
siłowni.”; logowanie się nie udaje.

### AUTH-06 · Sesja przetrwa zamknięcie przeglądarki — P2
**Kroki:** zaloguj się, zamknij całkowicie przeglądarkę, otwórz ją ponownie i wejdź na adres
aplikacji.
**Oczekiwany rezultat:** użytkownik nadal jest zalogowany (sesja trwa do 30 dni).

### AUTH-07 · Wylogowanie — P1
**Kroki:**
1. Zalogowany wejdź w „Więcej” → „Wyloguj się”.
2. Użyj przycisku „wstecz” przeglądarki.
3. Wklej adres chronionego ekranu, np. `/my-plan` albo `/admin/members`.

**Oczekiwany rezultat:** po wylogowaniu widać ekran logowania. Ani „wstecz”, ani wklejony adres nie
pokazują danych: aplikacja przekierowuje do `/login`.

### AUTH-08 · Wejście na chroniony adres bez logowania — P1
**Kroki:** w oknie prywatnym otwórz kolejno `/`, `/my-classes`, `/schedule`, `/admin/members`,
`/profile`.
**Oczekiwany rezultat:** za każdym razem następuje przekierowanie do ekranu logowania.

### AUTH-09 · Zablokowanie w trakcie sesji — P2
**Rola:** administrator (przeglądarka A) + członek (przeglądarka B)
**Warunki wstępne:** członek z kontem na twój adres (z `REG-01`) jest zalogowany w przeglądarce B.
**Kroki:**
1. W A zablokuj tego członka (`MEM-10`).
2. W B odśwież stronę lub przejdź na inny ekran.

**Oczekiwany rezultat:** w B członek traci dostęp: zostaje wylogowany albo aplikacja odmawia
danych. Nie może dalej korzystać z ekranów członka.

---

## Rejestracja z zaproszenia (REG)

### REG-01 · Pełna ścieżka: kartoteka → kod → rejestracja na własny e-mail — P1
**Rola:** administrator, potem osoba rejestrująca się
**Kroki:**
1. Jako administrator: Członkowie → „Dodaj członka”, wpisz imię i nazwisko (np. `QA Tester
   <data>`), zapisz.
2. Wystaw tej osobie karnet (`PASS-01`) i zapisz ją na przyszłe zajęcia (`BOOK-01`).
3. W menu akcji wiersza (ikona „⋯”) wybierz „Wygeneruj kod klubowicza”. Pojawi się kod i data „Ważny
   do …” (ok. 14 dni).
4. Kliknij „Kopiuj link” i otwórz go w oknie prywatnym.
5. Na ekranie „Załóż konto” sprawdź, że pole „Kod zaproszenia” jest wypełnione i tylko do odczytu.
6. Podaj **swój prawdziwy** e-mail i hasło (min. 8 znaków), kliknij „Załóż konto”.

**Oczekiwany rezultat:**
- Konto jest od razu aktywne, bez żadnego czekania na akceptację. Osoba trafia na Start.
- Na Starcie widać jej imię, kartę „Twój karnet” z wystawionym karnetem i „Najbliższe zajęcia” z
  zapisem z kroku 2.
- Na liście członków u administratora osoba nie ma już statusu „Bez konta”, a opcje kodu zniknęły
  z menu.

### REG-02 · Wejście na `/register` bez kodu — P1
**Kroki:** w oknie prywatnym otwórz `/register` (bez `?invitationCode=`).
**Oczekiwany rezultat:** przekierowanie na ekran logowania. W aplikacji nie ma żadnego linku
„Zarejestruj się”.

### REG-03 · Nieprawidłowy lub zmyślony kod — P1
**Kroki:** otwórz `/register?invitationCode=ABCDEF123` i spróbuj założyć konto.
**Oczekiwany rezultat:** rejestracja się nie udaje, komunikat „Ten link z zaproszeniem jest
nieprawidłowy. Poproś siłownię o nowy.” (albo przekierowanie na logowanie z komunikatem „To
zaproszenie już nie jest aktywne…”). Konto nie powstaje.

### REG-04 · Kod unieważniony i kod zastąpiony nowym — P2
**Kroki:**
1. Wygeneruj kod dla kartoteki bez konta i skopiuj link.
2. W menu wybierz „Unieważnij kod”. Spróbuj zarejestrować się starym linkiem.
3. Wygeneruj kod, skopiuj link, potem wybierz „Wygeneruj nowy kod” i spróbuj użyć poprzedniego
   linku.

**Oczekiwany rezultat:** w obu przypadkach stary link jest odrzucany, tak jak w `REG-03`.

### REG-05 · Kod jest jednorazowy — P2
**Kroki:** po udanej rejestracji z `REG-01` otwórz ten sam link jeszcze raz w nowym oknie
prywatnym i spróbuj założyć drugie konto na inny e-mail.
**Oczekiwany rezultat:** odmowa jak w `REG-03`.

### REG-06 · E-mail już zajęty — P2
**Kroki:** z ważnym kodem spróbuj zarejestrować się adresem istniejącego konta (np.
`czlonek010@example.test`).
**Oczekiwany rezultat:** pod polem e-mail pojawia się „To konto już istnieje.” z linkiem „Zaloguj
się”. Kod nie zostaje zużyty, więc da się go użyć z innym adresem.

### REG-07 · Walidacja formularza rejestracji — P2
**Kroki:** z ważnym kodem wpisz błędny e-mail (`abc`), potem hasło 7-znakowe.
**Oczekiwany rezultat:** komunikaty przy polach; pod hasłem podpowiedź „Co najmniej 8 znaków.”;
konto nie powstaje.

### REG-08 · Limit prób rejestracji — P3
**Kroki:** z jednego połączenia wyślij formularz rejestracji 4 razy w ciągu 5 minut (np. z
błędnymi kodami).
**Oczekiwany rezultat:** czwarta próba kończy się komunikatem „Zbyt wiele prób. Odczekaj chwilę i
spróbuj ponownie.”. Po 5 minutach można próbować znowu.

---

## Hasło: przypomnienie, reset, zmiana (PWD)

### PWD-01 · Przypomnienie hasła — wysyłka linku — P1
**Warunki wstępne:** konto na prawdziwym e-mailu (z `REG-01`).
**Kroki:** na ekranie logowania kliknij „Nie pamiętasz hasła?”, wpisz swój e-mail i kliknij „Wyślij
link”.
**Oczekiwany rezultat:** komunikat „Jeśli konto o tym adresie istnieje, wysłaliśmy na nie link…”.
W ciągu kilku minut przychodzi e-mail z linkiem (sprawdź też spam i zanotuj w raporcie, jeśli tam
trafił).

### PWD-02 · Taka sama odpowiedź dla nieistniejącego adresu — P1
**Kroki:** poproś o link dla `nie-ma-mnie@example.test`.
**Oczekiwany rezultat:** komunikat jest identyczny jak w `PWD-01`. Nic nie zdradza, że konta nie
ma.

### PWD-03 · Ustawienie nowego hasła z linku — P1
**Kroki:**
1. Otwórz link z e-maila. Pojawi się ekran „Ustaw nowe hasło”.
2. Wpisz nowe hasło dwa razy i kliknij „Ustaw nowe hasło”.
3. Zaloguj się nowym hasłem, a potem spróbuj starym.

**Oczekiwany rezultat:** nowe hasło działa, stare już nie.

### PWD-04 · Link użyty drugi raz lub uszkodzony — P2
**Kroki:** otwórz link z `PWD-03` ponownie. Osobno otwórz link z obciętym parametrem `token`.
**Oczekiwany rezultat:** „Ten link jest nieprawidłowy lub został już wykorzystany…” oraz link
„Wyślij nowy link”.

### PWD-05 · Walidacja nowego hasła — P2
**Kroki:** na ekranie resetu wpisz różne hasła w obu polach; potem hasło krótsze niż 8 znaków.
**Oczekiwany rezultat:** „Hasła nie są takie same.”; przy za krótkim haśle komunikat przy polu;
hasło się nie zmienia.

### PWD-06 · Ograniczenie wysyłki linków — P3
**Kroki:** poproś o link dla swojego adresu 3 razy w ciągu minuty.
**Oczekiwany rezultat:** na ekranie za każdym razem ten sam komunikat, ale do skrzynki trafia
najwyżej jeden e-mail na 2 minuty. Po 6 szybkich próbach w ciągu minuty z jednego połączenia
pojawia się „Zbyt wiele prób…”.

### PWD-07 · Zmiana hasła po zalogowaniu — P1
**Rola:** dowolna
**Kroki:** Więcej → Moje konto → sekcja „Zmiana hasła”: podaj obecne hasło, nowe i powtórzone,
kliknij „Zmień hasło”. Wyloguj się i zaloguj nowym hasłem.
**Oczekiwany rezultat:** pojawia się potwierdzenie (komunikat „toast”), a logowanie nowym hasłem
działa.
**Uwaga:** na kontach `@example.test` po teście przywróć wspólne hasło, żeby nie zablokować innych
testów.

### PWD-08 · Błędne obecne hasło przy zmianie — P2
**Kroki:** w „Zmiana hasła” podaj złe obecne hasło.
**Oczekiwany rezultat:** przy polu pojawia się „Obecne hasło jest nieprawidłowe.”; hasło się nie
zmienia.

---

## Nawigacja, persony i uprawnienia (NAV)

### NAV-01 · Menu każdej persony — komputer — P1
**Kroki:** zaloguj się kolejno jako członek, trener, `admin1` i sprawdź menu w nagłówku (ekran
szerszy niż 480 px).
**Oczekiwany rezultat:**
- Członek: Start, Zajęcia, Plan.
- Trener: Start, Grafik, Członkowie.
- Administrator: Start, Grafik, Członkowie, Typy zajęć, Ćwiczenia.
- Każdy ma dostęp do „Moje konto” i „Wyloguj się”.

### NAV-02 · Menu każdej persony — telefon (dolny pasek) — P1
**Kroki:** jak `NAV-01`, na telefonie (≤ 480 px).
**Oczekiwany rezultat:**
- Członek: Start, Zajęcia, Plan, Więcej.
- Trener: Start, Grafik, Członkowie, Więcej.
- Administrator: Start, Grafik, Członkowie, Ćwiczenia, Więcej; „Typy zajęć” są w „Więcej” w
  sekcji „Panel”.
- W „Więcej” każda persona ma kartę „Moje konto” (z imieniem i e-mailem) oraz „Wyloguj się”.
- Nagłówkowe menu i dolny pasek **nigdy** nie są widoczne jednocześnie, również przy
  przeciąganiu szerokości okna w okolicy 480 px.

### NAV-03 · Członek nie wejdzie na ekrany obsługi — P1
**Rola:** członek
**Kroki:** wpisz ręcznie adresy `/schedule`, `/admin/members`, `/admin/classes`,
`/admin/exercises`, `/trainer/members`.
**Oczekiwany rezultat:** żaden z tych ekranów się nie otwiera (aplikacja przekierowuje, np. na
Start), a dane obsługi nie pojawiają się nawet na chwilę.

### NAV-04 · Trener nie wejdzie na ekrany członka i administratora — P1
**Rola:** trener
**Kroki:** wpisz ręcznie `/my-classes`, `/my-plan`, `/admin/members`, `/admin/class-types`.
**Oczekiwany rezultat:** żaden się nie otwiera.

### NAV-05 · Administrator nie widzi ekranów członka — P2
**Rola:** administrator
**Kroki:** wpisz ręcznie `/my-classes` i `/my-plan`.
**Oczekiwany rezultat:** żaden się nie otwiera; na Starcie administratora nie ma kart „Najbliższe
zajęcia” ani „Twój karnet”.

### NAV-06 · Administrator z rolą trenera to nadal administrator — P2
**Rola:** `admin2@example.test`
**Kroki:** zaloguj się i obejrzyj menu i Start.
**Oczekiwany rezultat:** menu administratora (jak w `NAV-01`); na Starcie sekcja „Twoje zajęcia” z
kartami „Dzisiaj” i „Nadchodzące”, w których są zajęcia prowadzone przez `admin2`. U `admin1` te
karty mówią „Nie prowadzisz dziś zajęć.” / „Nie prowadzisz zajęć w najbliższym tygodniu.”.

### NAV-07 · „Grafik” administratora zależy od szerokości — P2
**Kroki:** jako administrator kliknij „Grafik” na ekranie ≥ 1024 px, a potem na węższym (np.
tablet, telefon).
**Oczekiwany rezultat:** na komputerze otwiera się kalendarz zarządzania (`/admin/classes`), na
węższym ekranie grafik do przeglądania z listą zapisanych (`/schedule`).

### NAV-08 · Telefon: pasek tytułu i strzałka wstecz — P2
**Rola:** dowolna, telefon
**Kroki:** z ekranu listy (np. Plan → info przy ćwiczeniu; Członkowie → Karnety) wejdź w ekran
szczegółu.
**Oczekiwany rezultat:** na górze przypięty pasek z nazwą ekranu. Na ekranie szczegółu jest strzałka
wstecz, a dolnego paska w tym czasie **nie** ma. Strzałka wraca do ekranu, z którego przyszliśmy.
Tytuł karty przeglądarki odpowiada nazwie ekranu.

### NAV-09 · Telefon: systemowy przycisk „wstecz” (Android) — P2
**Kroki:**
1. Ze Startu stuknij zakładkę w dolnym pasku, potem inną zakładkę, potem systemowe „wstecz”.
2. Otwórz nakładkę (np. listę zapisanych na zajęcia) i naciśnij „wstecz”.

**Oczekiwany rezultat:** w kroku 1 „wstecz” wraca na Start, a nie przechodzi po kolei przez
wszystkie zakładki. W kroku 2 „wstecz” zamyka nakładkę i zostaje na tym samym ekranie.

### NAV-10 · Nieznany adres — P3
**Kroki:** zalogowany wpisz `/cos-czego-nie-ma`.
**Oczekiwany rezultat:** przekierowanie na Start, bez białej strony ani błędu.
