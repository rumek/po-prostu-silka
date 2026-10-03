# Grupy, grafik, zapisy i obecność

Konwencje (priorytety, statusy, format przypadku) i dane testowe: [README](README.md), [środowisko i dane testowe](environment.md).

## Grupy (TYPE)

> Identyfikatory `TYPE-NN` zostają bez zmian po zmianie nazwy pojęcia na „grupa” (S-33), bo
> odwołują się do nich inne przypadki i raporty z przebiegów.

### TYPE-01 · Dodanie grupy — P1
**Rola:** administrator
**Kroki:** Grupy → „Dodaj grupę” → nazwa, opis, domyślny czas (np. 60), domyślna liczba miejsc
(np. 12) → „Zapisz”.
**Oczekiwany rezultat:** grupa pojawia się na liście z czasem i liczbą miejsc; jest dostępna do wyboru
przy tworzeniu zajęć.

### TYPE-02 · Walidacja grupy — P2
**Kroki:** zapisz bez nazwy; z czasem 0 i 481; z liczbą miejsc 0 i 201; z nazwą istniejącej
aktywnej grupy.
**Oczekiwany rezultat:** „Podaj nazwę grupy.”, „Czas trwania musi mieścić się w zakresie
1–480 minut.”, „Liczba miejsc musi mieścić się w zakresie 1–200.”, „Ta nazwa jest już zajęta przez
inną aktywną grupę. Wybierz inną.”.

### TYPE-03 · Zmiana nazwy działa wstecz, zmiana miejsc nie — P2
**Kroki:** zmień nazwę grupy, która ma zaplanowane zajęcia, oraz jej domyślną liczbę miejsc.
**Oczekiwany rezultat:** nowa nazwa widnieje przy wszystkich zajęciach tej grupy (także przeszłych i
w historii członków); liczba miejsc już zaplanowanych zajęć **nie** zmienia się.

### TYPE-04 · Dezaktywacja i aktywacja — P2
**Kroki:** „Dezaktywuj” przy grupie z zaplanowanymi zajęciami; sprawdź listę, formularz nowych
zajęć i grafik; zaznacz „Pokaż nieaktywne”; kliknij „Aktywuj”.
**Oczekiwany rezultat:** grupa znika z listy i z wyboru przy nowych zajęciach, ale zaplanowane zajęcia
zostają w grafiku. Z „Pokaż nieaktywne” grupę widać z oznaczeniem „Nieaktywna”; po aktywacji wraca.

---

## Grafik i zarządzanie zajęciami — administrator (CLS)

> Kalendarz zarządzania wymaga ekranu **≥ 1024 px** i myszy.

### CLS-01 · Kalendarz tygodnia — P1
**Kroki:** otwórz Grafik (`/admin/classes`) na komputerze; przejdź na następny i poprzedni tydzień;
wróć do bieżącego.
**Oczekiwany rezultat:** siatka tygodnia 06:00–21:00 z zajęciami (nazwa, godzina); nawigacja między
tygodniami działa; przy tygodniu przeszłym widać „Ten tydzień już minął — możesz tylko sprawdzić i
poprawić obecność.” i nie da się w nim nic przeciągać ani tworzyć.

### CLS-02 · Dodanie zajęć formularzem — P1
**Kroki:** „Dodaj zajęcia” → wybierz grupę (czas i miejsca uzupełniają się z grupy), zmień liczbę
miejsc, podaj początek w przyszłości, wybierz prowadzącego → „Zapisz”.
**Oczekiwany rezultat:** zajęcia pojawiają się w kalendarzu we właściwym miejscu, z nazwą grupy i
zmienioną liczbą miejsc.

### CLS-03 · Walidacja zajęć — P2
**Kroki:** spróbuj zapisać: bez grupy / prowadzącego; z początkiem w przeszłości; w czasie
nakładającym się na **dowolne** inne zajęcia w klubie; z czasem 481 min; z 201 miejscami.
**Oczekiwany rezultat:** „Wybierz grupę.” / „Wybierz prowadzącego.”, „Nie można zaplanować
zajęć w przeszłości.”, „O tej porze są już inne zajęcia. Wybierz inny termin.”, komunikaty o
zakresach jak w `TYPE-02`.

### CLS-04 · Tworzenie zajęć przeciągnięciem po pustym czasie — P2
**Kroki:** przeciągnij myszą po pustym fragmencie dnia w przyszłości.
**Oczekiwany rezultat:** otwiera się nakładka „Nowe zajęcia” z dniem, godziną i czasem z
zaznaczenia; po wyborze grupy i prowadzącego i kliknięciu „Dodaj zajęcia” zajęcia pojawiają się w
siatce. „Anuluj” lub kliknięcie tła zamyka bez zapisu.

### CLS-05 · Przesuwanie i zmiana długości przeciągnięciem — P2
**Kroki:** przeciągnij istniejące przyszłe zajęcia na inną godzinę lub dzień; potem przeciągnij
dolną krawędź, żeby je wydłużyć; spróbuj upuścić je na inne zajęcia.
**Oczekiwany rezultat:** zmiany zapisują się i zostają po odświeżeniu. Upuszczenie na konflikt jest
odrzucone („O tej porze są już inne zajęcia…”), a zajęcia wracają na miejsce.

### CLS-06 · Akcje zajęć — nakładka — P1
**Kroki:** kliknij zajęcia krótkie (np. 30 min) i długie.
**Oczekiwany rezultat:** nakładka z nazwą, dniem, godzinami, prowadzącym, „Wolne miejsca X / Y” i
przyciskami „Edytuj”, „Powiel”, „Zapisani” oraz „Odwołaj” albo „Usuń”. Wszystkie akcje są dostępne
niezależnie od długości zajęć. Klawisz Tab nie wychodzi poza nakładkę; po zamknięciu fokus wraca do
kalendarza.

### CLS-07 · Edycja zajęć — P2
**Kroki:** „Edytuj” → zmień godzinę, liczbę miejsc lub prowadzącego → „Zapisz”.
**Oczekiwany rezultat:** zmiany są widoczne w kalendarzu. Pola grupy nie da się zmienić (jest
adnotacja, że trzeba usunąć zajęcia i utworzyć nowe). Zapisani członkowie z prawdziwym e-mailem
dostają powiadomienie o zmianie (`NOTIF-02`).

### CLS-08 · Powielanie na kolejne tygodnie — P1
**Kroki:** „Powiel” → „Na kolejne tygodnie”: 3 → „Powiel”. Następnie powiel zajęcia na tydzień, w
którym o tej porze są już inne zajęcia. Spróbuj też 0 i 9 tygodni.
**Oczekiwany rezultat:** powstają kopie w kolejnych tygodniach o tej samej porze. Tygodnie z
konfliktem są **pominięte i wymienione** w komunikacie, a reszta powstaje. Dla 0 i 9: „Liczba
tygodni musi mieścić się w zakresie 1–8.”.

### CLS-09 · Usunięcie zajęć bez zapisów — P2
**Kroki:** u zajęć bez zapisanych: „Usuń” → „Tak, usuń”.
**Oczekiwany rezultat:** zajęcia znikają z kalendarza.

### CLS-10 · Zajęć z zapisami nie da się usunąć — odwołanie — P1
**Kroki:** u zajęć z zapisanymi spróbuj usunąć; kliknij „Odwołaj zamiast tego” (albo od razu
„Odwołaj”) → „Tak, odwołaj”.
**Oczekiwany rezultat:** przy próbie usunięcia pojawia się „Na te zajęcia ktoś się już zapisał,
więc nie można ich usunąć.” z propozycją odwołania. Ekran potwierdzenia mówi „Powiadomimy N
zapisanych osób e-mailem i powiadomieniem. Tej operacji nie można cofnąć.”. Po odwołaniu zajęcia są
widoczne jako odwołane (nie znikają), zapisani je tracą, a ich karnety odzyskują wejścia
(`PASS-08`). Członkowie dostają powiadomienie (`NOTIF-01`).

### CLS-11 · Ekran zarządzania na telefonie — P2
**Kroki:** otwórz `/admin/classes` na telefonie lub w oknie węższym niż 1024 px.
**Oczekiwany rezultat:** komunikat „Grafik zajęć edytujesz na komputerze — układanie zajęć w siatce
wymaga myszy.” z przyciskami „Zobacz grafik zajęć”, „Dodaj zajęcia” (formularz działa na telefonie)
i linkiem „Grupy”.

### CLS-12 · Brak grup lub trenerów — P3
**Kroki:** (tylko jeśli da się to bezpiecznie przygotować, np. po zgodzie na dezaktywację wszystkich
grup w środowisku) otwórz „Dodaj zajęcia”.
**Oczekiwany rezultat:** „Najpierw zdefiniuj grupę — zajęcia powstają z definicji.” z
przyciskiem „Przejdź do grup”. Jeżeli przygotowanie wymagałoby psucia wspólnych danych,
oznacz TC jako **Pominięty**.

---

## Grafik obsługi, zapisy i obecność (SCH, BOOK, ATT)

### SCH-01 · Grafik na telefonie — widok dnia — P1
**Rola:** trener lub administrator, telefon
**Kroki:** otwórz „Grafik”.
**Oczekiwany rezultat:** widać jeden dzień (dzisiejszy), pasek dni tygodnia do przełączania,
kontrolkę skoku do wybranej daty i siatkę godzin z zajęciami. Dzień bez zajęć mówi o tym wprost.

### SCH-02 · Grafik szerzej — widok tygodnia — P2
**Kroki:** otwórz Grafik na ekranie ≥ 768 px; zwężaj okno poniżej 768 px.
**Oczekiwany rezultat:** od 768 px widać cały tydzień, poniżej jeden dzień.

### SCH-03 · Trener widzi tylko swoje zajęcia — P1
**Rola:** `trener1`, potem `trener2`, potem administrator
**Oczekiwany rezultat:** trener widzi wyłącznie zajęcia, które prowadzi (z podpowiedzią na górze),
a administrator widzi wszystkie. Zajęcia `trener2` nie pojawiają się u `trener1`.

### BOOK-01 · Zapisanie członka na zajęcia — P1
**Rola:** administrator (dowolne zajęcia), trener (swoje zajęcia)
**Warunki wstępne:** członek z ważnym karnetem z wolnymi wejściami; przyszłe zajęcia z wolnymi
miejscami.
**Kroki:** Grafik → kliknij zajęcia → w nakładce „Zapisani na …” w polu „Dopisz członka” wpisz imię
lub e-mail → wybierz osobę z listy → „Zapisz”.
**Oczekiwany rezultat:** osoba pojawia się na liście zapisanych z datą zapisu; liczba wolnych miejsc
spada o 1; w jej karnecie zostaje o 1 wejście mniej. Członek widzi zajęcia w „Moje zajęcia” i na
Starcie. Osoby bez konta mają dopisek „— bez konta”.

### BOOK-02 · Lista zapisanych — P2
**Oczekiwany rezultat:** przy każdej osobie imię i nazwisko, e-mail i czas zapisu; przy braku
zapisów „Nikt nie jest jeszcze zapisany na te zajęcia.”.

### BOOK-03 · Odmowy zapisu — P1
Każdy podpunkt to osobna próba zapisu w nakładce z `BOOK-01`:

| Sytuacja | Oczekiwany komunikat |
| --- | --- |
| osoba bez karnetu ważnego w dniu zajęć (także karnet wygasły) | „Ta osoba nie ma karnetu ważnego w dniu tych zajęć.” |
| karnet bez wolnych wejść (6 takich osób w danych) | „Karnet tej osoby nie ma już wolnych wejść.” |
| zajęcia pełne (3 takie od jutra w danych) | „Brak wolnych miejsc na tych zajęciach.” |
| osoba już zapisana na te zajęcia | „Ta osoba jest już zapisana na te zajęcia.” (albo brak jej na liście do wyboru) |
| osoba zablokowana | „Ta osoba jest zablokowana i nie może być zapisana na zajęcia.” (albo brak jej na liście) |
| trener lub administrator jako uczestnik | „Trenerów i administratorów nie zapisuje się na zajęcia jako uczestników.” (albo brak na liście) |

**Oczekiwany rezultat (wspólny):** komunikat jest widoczny w nakładce, a liczba miejsc i stan
karnetu pozostają bez zmian.

### BOOK-04 · Zwolnienie miejsca przed zajęciami — P1
**Kroki:** u zapisanej osoby na przyszłych zajęciach kliknij „Zwolnij miejsce”.
**Oczekiwany rezultat:** osoba znika z listy, wolnych miejsc przybywa, a wejście wraca do karnetu.
Członek nie widzi już tych zajęć w „Nadchodzące”.

### BOOK-05 · Zapisy zamknięte po rozpoczęciu zajęć — P2
**Kroki:** otwórz zajęcia, które już się rozpoczęły (np. dzisiejsze wcześniejsze).
**Oczekiwany rezultat:** nie ma pola „Dopisz członka” ani przycisku „Zwolnij miejsce”; zamiast nich
są przyciski obecności (`ATT-01`).

### BOOK-06 · Brak nadmiarowych zapisów przy jednoczesnych zapisach — P1
**Rola:** dwóch testerów albo dwie przeglądarki zalogowane jako administrator
**Warunki wstępne:** zajęcia z **jednym** wolnym miejscem; dwie różne osoby z ważnymi karnetami.
**Kroki:** w obu przeglądarkach otwórz nakładkę tych zajęć, wybierz różne osoby i kliknij „Zapisz”
możliwie jednocześnie (np. odliczając).
**Oczekiwany rezultat:** udaje się **dokładnie jeden** zapis; drugi dostaje „Brak wolnych miejsc na
tych zajęciach.” albo „Ktoś właśnie zmienił zapisy na te zajęcia. Spróbuj ponownie.”. Po
odświeżeniu liczba zapisanych nigdy nie przekracza liczby miejsc. Powtórz 3 razy.

### BOOK-07 · Ostatnie wejście karnetu przy jednoczesnych zapisach — P2
**Warunki wstępne:** osoba z karnetem z **jednym** wolnym wejściem; dwoje przyszłych zajęć z wolnymi
miejscami.
**Kroki:** w dwóch przeglądarkach zapisz tę osobę jednocześnie na dwa różne zajęcia.
**Oczekiwany rezultat:** udaje się tylko jeden zapis; drugi dostaje „Karnet tej osoby nie ma już
wolnych wejść.” (lub komunikat o konflikcie). Karnet nigdy nie schodzi poniżej 0.

### BOOK-08 · Trener zapisuje tylko na swoje zajęcia — P2
**Rola:** trener
**Oczekiwany rezultat:** trener może zapisać członka na zajęcia, które prowadzi; cudzych zajęć nie
widzi w grafiku, więc nie ma jak na nie zapisać.

### BOOK-09 · Duża lista kandydatów — P3
**Kroki:** w „Dopisz członka” wpisz jedną częstą literę (np. `a`).
**Oczekiwany rezultat:** gdy pasujących jest więcej, niż mieści lista, pojawia się „Pokazano X z Y
pasujących — zawęź wyszukiwanie.”.

### ATT-01 · Oznaczanie obecności — P1
**Rola:** trener (swoje zajęcia) lub administrator
**Warunki wstępne:** zajęcia, które już się rozpoczęły, z zapisanymi osobami.
**Kroki:** w nakładce zapisanych kliknij „Obecny” przy jednej osobie, „Odrobi” przy drugiej i
„Przepada” przy trzeciej.
**Oczekiwany rezultat:** oznaczenia są widoczne od razu; licznik „Obecni: X · Odrobią: Y · Przepada:
Z · Nieoznaczeni: W” się aktualizuje; po zamknięciu i ponownym otwarciu stan jest zachowany. Członek
widzi wynik w „Historia” (`MBR-02`): „Obecny”, „Do odrobienia”, „Nieobecny”.

### ATT-02 · Każdy wynik obecności zużywa wejście — P1
**Kroki:** zanotuj pozostałe wejścia członka. Oznacz go po kolei „Odrobi”, „Przepada” i „Obecny”,
sprawdzając karnet po każdej zmianie.
**Oczekiwany rezultat:** wejście jest zużyte przy każdym z trzech oznaczeń — liczba się nie zmienia
(S-36: tak liczy klub; „odrobi” daje za to jedno darmowe odrabianie, `MAKEUP-01`).

### ATT-03 · Stara nieobecność i korekta przy braku wejść — P2
**Warunki wstępne:** zapis oznaczony „Nieobecny” sprzed S-36 (np. w danych sprzed wdrożenia) — jego
wejście wróciło na karnet.
**Kroki:** otwórz te zajęcia; zużyj wolne wejście zapisem na inne zajęcia, a potem oznacz starą
nieobecność „Obecny” (albo „Odrobi” / „Przepada”).
**Oczekiwany rezultat:** wiersz pokazuje „Nieobecny — wejście zwrócone”, żaden przycisk nie jest
wciśnięty. Przy braku wejść korekta jest odrzucona: „Karnet tej osoby nie ma już wolnych wejść.”.

### ATT-04 · „Wszyscy obecni” — P2
**Kroki:** na rozpoczętych zajęciach z kilkoma nieoznaczonymi osobami kliknij „Wszyscy obecni”.
**Oczekiwany rezultat:** wszystkie osoby mają oznaczenie „Obecny”, licznik się zgadza, a przycisk
znika.

### ATT-05 · Obecności nie da się oznaczyć przed rozpoczęciem — P2
**Oczekiwany rezultat:** przy przyszłych zajęciach nie ma przycisków obecności (jest „Zwolnij
miejsce”).

### ATT-06 · Korekta obecności w przeszłym tygodniu — P2
**Rola:** administrator
**Kroki:** w kalendarzu przejdź na poprzedni tydzień, kliknij zajęcia → „Zapisani”, zmień
oznaczenie.
**Oczekiwany rezultat:** korekta się zapisuje (obecność można poprawiać bez limitu czasu), mimo że
tydzień jest tylko do odczytu w pozostałym zakresie.

### ATT-07 · Podwójne kliknięcie — P3
**Kroki:** kliknij szybko dwa razy ten sam przycisk obecności (na telefonie stuknij dwa razy).
**Oczekiwany rezultat:** brak błędu i brak podwójnego zużycia wejścia.

## Odrabianie (MAKEUP)

S-36 class-makeups. Lista „Odrabianie” jest wspólna dla administratora i trenerów.

### MAKEUP-01 · Darmowe odrabianie u innego trenera — P1
**Rola:** trener
**Warunki wstępne:** członek z karnetem i nieobecnością oznaczoną „Odrobi” na zajęciach z ostatnich
dni; nadchodzące zajęcia INNEGO trenera z wolnym miejscem w ciągu 30 dni.
**Kroki:** otwórz „Odrabianie”, przy członku „Zapisz”, wybierz zajęcia innego trenera i
„Zapisz”. Jako tamten trener oznacz członka na tych zajęciach „Obecny”.
**Oczekiwany rezultat:** pozycja przechodzi „Do odrobienia” → „Zaplanowane” → „Odrobione”; w rosterze
zajęć odrabiających członek ma etykietę „Odrabianie” i tylko dwa przyciski (bez „Odrobi”); liczba
wejść na karnecie się nie zmienia.

### MAKEUP-02 · Wybór zajęć pokazuje tylko to, co da się zapisać — P2
**Kroki:** otwórz wybór zajęć dla pozycji „Do odrobienia”.
**Oczekiwany rezultat:** brak zajęć pełnych, zajęć, na które członek już jest zapisany, zajęć po
terminie odrobienia i zajęć w dniu, którego nie pokrywa żaden karnet członka. Bez pasujących zajęć —
„Brak zajęć z wolnym miejscem w terminie odrabiania.”.

### MAKEUP-03 · Zwolnienie i odwołanie odrabiania — P1
**Kroki:** (a) przy pozycji „Zaplanowane” kliknij „Zwolnij”; (b) zapisz ponownie i odwołaj zajęcia
odrabiające jako administrator.
**Oczekiwany rezultat:** w obu przypadkach pozycja wraca do „Do odrobienia” i można ją zapisać
ponownie.

### MAKEUP-04 · Zamknięcie i ponowne otwarcie — P2
**Kroki:** przy pozycji „Do odrobienia” kliknij „Nie odrobi”, zaznacz „Pokaż zamknięte”, kliknij
„Otwórz ponownie”.
**Oczekiwany rezultat:** po zamknięciu pozycja znika z listy roboczej i jest widoczna jako „Nie
odrobione · Zamknięte ręcznie”; po otwarciu wraca jako „Do odrobienia”. Po upływie terminu przycisku
„Otwórz ponownie” nie ma.

### MAKEUP-05 · Nieobecności z zapisanym odrabianiem nie da się przeoznaczyć — P2
**Kroki:** przy nieobecności „Odrobi” z zapisanym odrabianiem kliknij w rosterze „Obecny”.
**Oczekiwany rezultat:** odmowa na wierszu: „Ta osoba ma już zapisane odrabianie tych zajęć. Zwolnij
je w „Odrabianiu”, aby zmienić obecność.”; oznaczenie zostaje „Odrobi”.

### MAKEUP-06 · Członek widzi, co ma do odrobienia — P2
**Rola:** członek
**Oczekiwany rezultat:** w „Moje zajęcia” jest panel „Do odrobienia: N” z najbliższym terminem; zapis
odrabiający na liście nadchodzących ma etykietę „Odrabianie”. Po zapisaniu odrabiania panel znika.

### MAKEUP-07 · Lista i wybór zajęć na różnych szerokościach — P3
**Oczekiwany rezultat:** lista „Odrabianie” i nakładka wyboru zajęć czytelne na telefonie, tablecie
i desktopie; na telefonie nakładka jest pełnym ekranem, a „wstecz” ją zamyka.

## Stały skład grupy (ROSTER)

S-37: grupa ma stały skład (do liczby miejsc grupy), wpisywany raz. Każde automatyczne zapisanie
przechodzi te same bramki co zapis przez obsługę (karnet ważny w dniu zajęć z wolnym wejściem, wolne
miejsce, nie pracownik); to, czego nie da się zapisać, jest pomijane i pokazane w panelu „Nie
wszystkich zapisano”.

### ROSTER-01 · Dodanie osoby do składu zapisuje ją na nadchodzące zajęcia — P1
**Rola:** administrator
**Warunki wstępne:** grupa z co najmniej dwojgiem nadchodzących zajęć; członek z karnetem
obejmującym ich daty.
**Kroki:** „Grupy” → „Skład (n/m)” przy grupie → wyszukaj członka → „Dopisz”.
**Oczekiwany rezultat:** członek jest na liście składu z „zapisany na N z N zajęć” i bez braków;
komunikat „Dodano do składu. Dopisano na N zajęć”; członek widnieje na liście zapisanych każdych
z tych zajęć i w swoich „Moje zajęcia”. Licznik przy grupie rośnie o jeden.

### ROSTER-02 · Osoba bez karnetu zostaje w składzie i widać, czego brakuje — P1
**Rola:** administrator
**Warunki wstępne:** członek bez karnetu (lub z karnetem kończącym się przed częścią zajęć).
**Kroki:** dopisz go do składu grupy.
**Oczekiwany rezultat:** członek jest w składzie; panel „Nie wszystkich zapisano” wymienia go z datami
i zdaniem „Ta osoba nie ma karnetu ważnego w dniu tych zajęć.”; ten sam brak widać przy jego wierszu
po odświeżeniu ekranu. Panel zamyka się przyciskiem X.

### ROSTER-03 · Usunięcie ze składu zwalnia przyszłe zapisy, ale nie odrabianie — P1
**Rola:** administrator
**Warunki wstępne:** członek składu zapisany na nadchodzące zajęcia grupy, w tym na jedne jako
odrabianie (S-36), oraz z zajęciami grupy już odbytymi.
**Kroki:** „Usuń ze składu” → potwierdź „Usuń”.
**Oczekiwany rezultat:** członek znika ze składu; jego zapisy na nadchodzące zajęcia grupy są
zwolnione, a wejścia wracają na karnet; zapis odrabiający i odbyte zajęcia (z obecnością) zostają.

### ROSTER-04 · „Uzupełnij zapisy” po wydaniu karnetu poza aplikacją albo zwolnieniu miejsca — P2
**Rola:** administrator lub trener
**Warunki wstępne:** członek składu z brakiem „można zapisać — uzupełnij zapisy” (np. zwolniło się
miejsce na pełnych zajęciach).
**Kroki:** „Uzupełnij zapisy”.
**Oczekiwany rezultat:** braki „można zapisać” znikają, komunikat podaje liczbę dopisanych zajęć;
drugie kliknięcie nic nie dopisuje („Nie było kogo dopisać.”).

### ROSTER-05 · Powielenie zajęć grupy zapisuje skład na kopie — P1
**Rola:** administrator
**Warunki wstępne:** grupa ze składem; jeden członek ma karnet tylko na najbliższe 2 tygodnie.
**Kroki:** w „Zajęcia” otwórz zajęcia grupy → „Powiel” na 4 tygodnie.
**Oczekiwany rezultat:** komunikat „Utworzono 4 kopie … Zapisano ze składu grupy: N.”; panel „Nie
wszystkich zapisano” nad kalendarzem wymienia członka z datami tygodni poza karnetem. To samo przy
dodaniu zajęć formularzem „Dodaj zajęcia” i przeciągnięciem w kalendarzu.

### ROSTER-06 · Nowy karnet zapisuje na kolejne tygodnie — P1
**Rola:** administrator
**Warunki wstępne:** członek z ROSTER-05, z brakami „brak karnetu” na późniejszych zajęciach grupy.
**Kroki:** wydaj mu kolejny karnet obejmujący te daty (albo przesuń datę końca obecnego).
**Oczekiwany rezultat:** brakujące zajęcia zostają zapisane bez dodatkowego kliknięcia; na ekranie
karnetu liczba wykorzystanych wejść uwzględnia te zapisy; panel pojawia się tylko wtedy, gdy czegoś
nie dało się zapisać (np. brak miejsc).

### ROSTER-07 · Trener zarządza składem tylko swoich grup i tylko na swoich zajęciach — P2
**Rola:** trener
**Kroki:** „Więcej” → „Grupy”; otwórz grupę; dopisz osobę. Wpisz w adres URL grupę, której zajęć nie
prowadzi.
**Oczekiwany rezultat:** lista zawiera tylko grupy, których nadchodzące zajęcia prowadzi; dopisanie
zapisuje tylko na jego zajęcia, a zajęcia innego trenera pokazuje jako brak „Te zajęcia prowadzi inny
trener — zapisuje na nie administrator.”; cudza grupa pokazuje komunikat o braku uprawnień.

### ROSTER-08 · Ekran składu na różnych szerokościach — P3
**Oczekiwany rezultat:** ekran składu i lista „Grupy” trenera są czytelne na telefonie, tablecie
i desktopie, bez przewijania w poziomie; na telefonie pasek u góry pokazuje „Skład — <grupa>” i
strzałkę wstecz.
