namespace po_prostu_silka.Infrastructure.TestData;

/// <summary>
/// The static vocabulary <see cref="TestDataGenerator"/> draws from: Polish names, streets and
/// cities so the UI reads like a real club, and the exercise and group catalogues.
///
/// <para>
/// ORDER IS PART OF THE CONTRACT. The generator picks by index from a fixed-seed Random, so
/// reordering, inserting into or removing from any list below changes every seeded person and row.
/// That is harmless (a reseed simply produces a different club), but it breaks "a reseed yields the
/// same data" across the change - append rather than insert when it matters.
/// </para>
/// </summary>
internal static class TestDataNames
{
    public static readonly string[] FemaleFirstNames =
    [
        "Anna", "Maria", "Katarzyna", "Małgorzata", "Agnieszka", "Barbara", "Ewa", "Magdalena",
        "Joanna", "Aleksandra", "Monika", "Zofia", "Natalia", "Karolina", "Justyna", "Paulina",
        "Marta", "Weronika", "Julia", "Dominika", "Beata", "Iwona", "Patrycja", "Alicja",
    ];

    public static readonly string[] MaleFirstNames =
    [
        "Piotr", "Krzysztof", "Andrzej", "Tomasz", "Paweł", "Michał", "Marcin", "Jakub",
        "Adam", "Łukasz", "Mateusz", "Grzegorz", "Kamil", "Wojciech", "Maciej", "Rafał",
        "Bartosz", "Dariusz", "Sebastian", "Szymon", "Filip", "Damian", "Karol", "Jan",
    ];

    /// <summary>Masculine surname and its feminine form, so a first name always gets a matching one.</summary>
    public static readonly (string Male, string Female)[] Surnames =
    [
        ("Nowak", "Nowak"), ("Kowalski", "Kowalska"), ("Wiśniewski", "Wiśniewska"),
        ("Wójcik", "Wójcik"), ("Kowalczyk", "Kowalczyk"), ("Kamiński", "Kamińska"),
        ("Lewandowski", "Lewandowska"), ("Zieliński", "Zielińska"), ("Szymański", "Szymańska"),
        ("Woźniak", "Woźniak"), ("Dąbrowski", "Dąbrowska"), ("Kozłowski", "Kozłowska"),
        ("Jankowski", "Jankowska"), ("Mazur", "Mazur"), ("Kwiatkowski", "Kwiatkowska"),
        ("Krawczyk", "Krawczyk"), ("Piotrowski", "Piotrowska"), ("Grabowski", "Grabowska"),
        ("Nowakowski", "Nowakowska"), ("Pawłowski", "Pawłowska"), ("Michalski", "Michalska"),
        ("Nowicki", "Nowicka"), ("Adamczyk", "Adamczyk"), ("Dudek", "Dudek"),
        ("Zając", "Zając"), ("Wieczorek", "Wieczorek"), ("Jabłoński", "Jabłońska"),
        ("Król", "Król"), ("Majewski", "Majewska"), ("Olszewski", "Olszewska"),
        ("Jaworski", "Jaworska"), ("Wróbel", "Wróbel"), ("Malinowski", "Malinowska"),
        ("Pawlak", "Pawlak"), ("Witkowski", "Witkowska"), ("Walczak", "Walczak"),
    ];

    public static readonly string[] Streets =
    [
        "Marszałkowska", "Piotrkowska", "Długa", "Krakowska", "Mickiewicza", "Słowackiego",
        "Kościuszki", "Polna", "Leśna", "Ogrodowa", "Lipowa", "Kwiatowa", "Szkolna", "Parkowa",
        "Sienkiewicza", "Żeromskiego", "Reymonta", "Narutowicza", "Wojska Polskiego", "Jana Pawła II",
    ];

    /// <summary>City and a plausible postal code for it, in the <c>00-000</c> form ContactDetails accepts.</summary>
    public static readonly (string City, string PostalCode)[] Cities =
    [
        ("Warszawa", "00-950"), ("Warszawa", "02-495"), ("Warszawa", "03-301"),
        ("Kraków", "30-001"), ("Łódź", "90-001"), ("Wrocław", "50-001"), ("Poznań", "60-001"),
        ("Gdańsk", "80-001"), ("Piaseczno", "05-500"), ("Pruszków", "05-800"),
    ];

    /// <summary>
    /// What the club sells, as its attendance spreadsheet (treningi.xlsx) has it: a fixed weekly slot
    /// for one person, a pair, a trio or a group of up to six. <see cref="NamePrefix"/> is null for the
    /// individual kind, which is named after its one person instead of its slot.
    /// </summary>
    public sealed record GroupKindSpec(
        string? NamePrefix,
        int Capacity,
        int DurationMinutes,
        int Count,
        string Description);

    public static readonly GroupKindSpec[] GroupKinds =
    [
        new(null, 1, 60, 10, "Trening indywidualny – stały termin raz w tygodniu."),
        new("Para", 2, 60, 8, "Trening w parze – stały termin raz w tygodniu."),
        new("Trio", 3, 60, 5, "Trening w trzy osoby – stały termin raz w tygodniu."),
        new("Grupa", 6, 75, 7, "Grupa do 6 osób – stały termin raz w tygodniu."),
    ];

    /// <summary>
    /// Club-local start times on the spreadsheet's 15-minute grid. CONSECUTIVE ENTRIES ARE AT LEAST 75
    /// MINUTES APART - the longest group - so any subset of them is overlap-free under the app's
    /// club-wide rule (HasTimeConflictAsync), and an unused one is always a free time to move a class to.
    /// </summary>
    public static readonly TimeOnly[] SlotTimes =
    [
        new(6, 15), new(7, 30), new(9, 0), new(10, 30), new(12, 0),
        new(16, 0), new(17, 15), new(18, 30), new(19, 45),
    ];

    /// <summary>Monday to Saturday; the club does not train on Sundays.</summary>
    public static readonly (DayOfWeek Day, string Short)[] TrainingDays =
    [
        (DayOfWeek.Monday, "Pon"), (DayOfWeek.Tuesday, "Wt"), (DayOfWeek.Wednesday, "Śr"),
        (DayOfWeek.Thursday, "Czw"), (DayOfWeek.Friday, "Pt"), (DayOfWeek.Saturday, "Sob"),
    ];

    /// <summary>The karnet types of the club's payment spreadsheet (twt.xlsx), with an entry count.</summary>
    public sealed record PassTypeSpec(string Name, int Entries, int ValidityDays);

    /// <summary>One training a week, so a 30-day month holds four - or five, the fifth then refused.</summary>
    public static readonly PassTypeSpec Monthly = new("Miesięczny", 4, 30);
    public static readonly PassTypeSpec OneOff = new("Wejście jednorazowe", 1, 30);
    public static readonly PassTypeSpec Voucher = new("Voucher", 4, 60);

    public sealed record ExerciseSpec(
        string Name,
        string MuscleGroup,
        string Difficulty,
        string? Equipment,
        string Description,
        string Preparation,
        string StartingPosition,
        string Execution,
        bool IsActive = true);

    public const string Beginner = "Początkujący";
    public const string Intermediate = "Średniozaawansowany";
    public const string Advanced = "Zaawansowany";

    /// <summary>
    /// Every URL here goes through <c>YouTubeVideoId.TryParse</c>, the admin form's own path. The
    /// three Shorts were supplied by the user on 2026-09-22; do not invent others.
    /// </summary>
    public static readonly string[] VideoUrls =
    [
        "https://www.youtube.com/shorts/FR8qYjA8pKQ",
        "https://www.youtube.com/shorts/qsmtzydAS_U",
        "https://www.youtube.com/shorts/C-tRbRSBAoE",
    ];

    /// <summary>The last two entries are inactive, so the library shows both states.</summary>
    public static readonly ExerciseSpec[] Exercises =
    [
        new("Wyciskanie sztangi na ławce płaskiej", "Klatka piersiowa", Intermediate, "Sztanga, ławka",
            "Podstawowe ćwiczenie wielostawowe na klatkę piersiową.",
            "Ustaw stojaki na wysokości wyprostowanych rąk.",
            "Leżenie na ławce, łopatki ściągnięte, stopy na podłodze.",
            "Opuść sztangę do dolnej części mostka, wypchnij ją do pełnego wyprostu łokci."),
        new("Pompki klasyczne", "Klatka piersiowa", Beginner, null,
            "Ćwiczenie z masą własnego ciała na klatkę i triceps.",
            "Znajdź miejsce na macie.",
            "Podpór przodem, dłonie nieco szerzej niż barki, ciało w linii prostej.",
            "Opuść klatkę nad podłogę, zachowując napięty brzuch, i wróć do podporu."),
        new("Rozpiętki z hantlami", "Klatka piersiowa", Intermediate, "Hantle, ławka",
            "Izolowane ćwiczenie rozciągające mięśnie piersiowe.",
            "Przygotuj dwie lekkie hantle.",
            "Leżenie na ławce, hantle nad klatką, łokcie lekko ugięte.",
            "Rozłóż ramiona łukiem do poziomu barków i zbierz je z powrotem nad klatką."),
        new("Martwy ciąg klasyczny", "Plecy", Advanced, "Sztanga",
            "Ćwiczenie całego tylnego łańcucha mięśniowego.",
            "Załaduj sztangę i ustaw ją nad śródstopiem.",
            "Stopy na szerokość bioder, chwyt nachwytem, plecy proste.",
            "Wyprostuj biodra i kolana jednocześnie, prowadząc sztangę blisko nóg."),
        new("Wiosłowanie hantlem w opadzie", "Plecy", Beginner, "Hantel, ławka",
            "Jednorącz na mięsień najszerszy grzbietu.",
            "Oprzyj kolano i dłoń o ławkę.",
            "Tułów równolegle do podłogi, hantel w wyprostowanej ręce.",
            "Przyciągnij hantel do biodra, ściągając łopatkę, i opuść go kontrolowanie."),
        new("Podciąganie na drążku", "Plecy", Advanced, "Drążek",
            "Klasyczne ćwiczenie na szerokość pleców.",
            "Chwyć drążek nachwytem nieco szerzej niż barki.",
            "Zwis na wyprostowanych rękach, łopatki aktywne.",
            "Podciągnij się, aż broda minie drążek, i opuść się do pełnego zwisu."),
        new("Ściąganie drążka wyciągu górnego", "Plecy", Beginner, "Wyciąg górny",
            "Alternatywa dla podciągania na maszynie.",
            "Ustaw wałek na wysokości ud.",
            "Siad, chwyt szeroki, lekkie odchylenie tułowia.",
            "Ściągnij drążek do górnej części klatki i wróć do wyprostu rąk."),
        new("Przysiad ze sztangą", "Nogi", Intermediate, "Sztanga, stojaki",
            "Podstawowe ćwiczenie na mięśnie nóg i pośladki.",
            "Ustaw sztangę na stojakach na wysokości barków.",
            "Sztanga na górnej części pleców, stopy na szerokość barków.",
            "Zejdź w dół, aż uda będą równoległe do podłogi, i wstań, wypychając biodra."),
        new("Wykroki z hantlami", "Nogi", Beginner, "Hantle",
            "Ćwiczenie jednonóż na uda i pośladki.",
            "Przygotuj dwie hantle.",
            "Stanie, hantle w opuszczonych rękach.",
            "Zrób krok w przód, opuść tylne kolano nad podłogę i wróć do stania."),
        new("Hip thrust", "Nogi", Intermediate, "Sztanga, ławka",
            "Najskuteczniejsze ćwiczenie na mięśnie pośladkowe.",
            "Oprzyj łopatki o ławkę, sztangę połóż na biodrach.",
            "Siad przy ławce, stopy na podłodze, kolana ugięte.",
            "Unieś biodra do linii tułowia, zatrzymaj na chwilę i opuść."),
        new("Wypychanie na suwnicy", "Nogi", Beginner, "Suwnica",
            "Bezpieczna alternatywa dla przysiadu.",
            "Ustaw oparcie i zabezpieczenia suwnicy.",
            "Stopy na platformie na szerokość bioder.",
            "Opuść platformę do kąta prostego w kolanach i wypchnij ją bez blokowania kolan."),
        new("Wspięcia na palce", "Nogi", Beginner, "Stopień",
            "Ćwiczenie na mięśnie łydek.",
            "Stań na krawędzi stopnia.",
            "Pięty poza stopniem, ręka oparta o ścianę.",
            "Wspinaj się na palce najwyżej jak możesz i opuszczaj pięty poniżej stopnia."),
        new("Wyciskanie hantli nad głowę", "Barki", Intermediate, "Hantle",
            "Ćwiczenie na mięśnie naramienne.",
            "Przygotuj dwie hantle.",
            "Siad lub stanie, hantle na wysokości barków.",
            "Wypchnij hantle nad głowę do wyprostu łokci i opuść je do barków."),
        new("Unoszenie hantli bokiem", "Barki", Beginner, "Hantle",
            "Izolacja środkowego aktonu barków.",
            "Wybierz lekkie hantle.",
            "Stanie, hantle przy udach, łokcie lekko ugięte.",
            "Unieś ramiona bokiem do wysokości barków i opuść powoli."),
        new("Face pull", "Barki", Beginner, "Wyciąg, lina",
            "Wzmacnia tylne aktony barków i poprawia postawę.",
            "Ustaw linę na wysokości twarzy.",
            "Stanie przodem do wyciągu, chwyt liny kciukami do tyłu.",
            "Przyciągnij linę do twarzy, rozchylając łokcie na boki."),
        new("Uginanie ramion ze sztangą", "Ramiona", Beginner, "Sztanga łamana",
            "Klasyczne ćwiczenie na biceps.",
            "Wybierz obciążenie pozwalające na czysty ruch.",
            "Stanie, sztanga podchwytem, łokcie przy tułowiu.",
            "Ugnij ramiona do wysokości barków i opuść sztangę do wyprostu."),
        new("Prostowanie ramion na wyciągu", "Ramiona", Beginner, "Wyciąg górny",
            "Izolacja tricepsa.",
            "Ustaw drążek na górze wyciągu.",
            "Stanie, łokcie przy tułowiu, drążek na wysokości klatki.",
            "Wyprostuj ramiona w dół i wróć, nie odrywając łokci od tułowia."),
        new("Pompki na poręczach", "Ramiona", Advanced, "Poręcze",
            "Wymagające ćwiczenie na triceps i klatkę.",
            "Chwyć poręcze i wejdź do podporu.",
            "Podpór na wyprostowanych rękach, tułów lekko pochylony.",
            "Opuść się do kąta prostego w łokciach i wypchnij do wyprostu."),
        new("Deska", "Brzuch", Beginner, "Mata",
            "Izometryczne ćwiczenie stabilizujące tułów.",
            "Rozłóż matę.",
            "Podpór na przedramionach, ciało w linii prostej.",
            "Utrzymaj pozycję, napinając brzuch i pośladki, bez unoszenia bioder."),
        new("Dead bug", "Brzuch", Beginner, "Mata",
            "Ćwiczenie koordynacji i stabilizacji odcinka lędźwiowego.",
            "Połóż się na macie.",
            "Leżenie tyłem, ręce w górze, nogi ugięte pod kątem prostym.",
            "Opuszczaj naprzemiennie przeciwną rękę i nogę, trzymając lędźwie przy podłodze."),
        new("Unoszenie nóg w zwisie", "Brzuch", Advanced, "Drążek",
            "Zaawansowane ćwiczenie na dolną część brzucha.",
            "Chwyć drążek nachwytem.",
            "Zwis na wyprostowanych rękach.",
            "Unieś proste nogi do poziomu bioder i opuść je bez bujania."),
        new("Russian twist", "Brzuch", Intermediate, "Piłka lekarska",
            "Ćwiczenie na mięśnie skośne brzucha.",
            "Przygotuj piłkę lekarską.",
            "Siad z odchylonym tułowiem, stopy nad podłogą.",
            "Skręcaj tułów naprzemiennie w obie strony, dotykając piłką podłogi."),
        new("Koci grzbiet", "Mobilność", Beginner, "Mata",
            "Mobilizacja całego kręgosłupa.",
            "Rozłóż matę.",
            "Klęk podparty, dłonie pod barkami, kolana pod biodrami.",
            "Na wydechu zaokrąglij plecy, na wdechu wygnij je w dół."),
        new("Rozciąganie zginaczy bioder", "Mobilność", Beginner, "Mata",
            "Przeciwdziała skutkom długiego siedzenia.",
            "Podłóż matę pod kolano.",
            "Klęk jednonóż, druga stopa z przodu.",
            "Przesuń biodra w przód, utrzymując prosty tułów, i przytrzymaj 30 sekund."),
        new("Rotacje piersiowe w klęku", "Mobilność", Beginner, "Mata",
            "Poprawia ruchomość odcinka piersiowego.",
            "Rozłóż matę.",
            "Klęk podparty, jedna dłoń za głową.",
            "Obracaj tułów, kierując łokieć do sufitu, i wracaj łokciem do przeciwnej ręki."),
        new("Wyciskanie na maszynie Smitha", "Klatka piersiowa", Beginner, "Maszyna Smitha",
            "Ćwiczenie wycofane z planów - maszyna w serwisie.",
            "Ustaw ławkę pod gryfem.",
            "Leżenie na ławce pod gryfem.",
            "Opuść gryf do klatki i wypchnij go w górę.",
            IsActive: false),
        new("Brzuszki na maszynie", "Brzuch", Beginner, "Maszyna",
            "Ćwiczenie wycofane - zastąpione przez dead bug.",
            "Ustaw oparcie maszyny.",
            "Siad w maszynie, dłonie na uchwytach.",
            "Zegnij tułów w przód i wróć do pozycji wyjściowej.",
            IsActive: false),
    ];

    public static readonly string[] PlanNames =
    [
        "FBW dla początkujących",
        "Siła - góra/dół",
        "Zdrowe plecy",
        "Powrót po przerwie",
        "Pośladki i nogi",
        "Push / pull",
        "Mobilność na co dzień",
        "Przygotowanie do biegu",
    ];

    public static readonly string[] ItemNotes =
    [
        "Tempo 3-1-1, kontroluj fazę opuszczania.",
        "Ostatnia seria do upadku technicznego.",
        "Zwiększ ciężar, gdy zrobisz wszystkie powtórzenia.",
        "Pilnuj neutralnego ustawienia kręgosłupa.",
        "Oddech: wydech przy wysiłku.",
    ];
}
