using po_prostu_silka.Application.Members;
using po_prostu_silka.Domain;
using po_prostu_silka.Domain.Members;
using po_prostu_silka.Domain.Scheduling;
using po_prostu_silka.Domain.Training;

namespace po_prostu_silka.Infrastructure.TestData;

/// <summary>An Identity account the seeder must create, with the roles it joins.</summary>
public sealed record SeedAccount(ApplicationUser User, IReadOnlyList<string> Roles);

/// <summary>The whole seeded club, as plain entities. Plan items travel inside their plans.</summary>
public sealed record TestDataSet(
    IReadOnlyList<SeedAccount> Accounts,
    IReadOnlyList<Member> Members,
    IReadOnlyList<MembershipPass> Passes,
    IReadOnlyList<ClassType> ClassTypes,
    IReadOnlyList<Class> Classes,
    IReadOnlyList<Booking> Bookings,
    IReadOnlyList<Exercise> Exercises,
    IReadOnlyList<TrainingPlan> Plans);

/// <summary>
/// Builds the test club (S-24) in memory. Knows nothing about the database: <see cref="TestDataSeeder"/>
/// persists what this returns.
///
/// <para>
/// DETERMINISTIC FOR A GIVEN SEED AND CLUB-LOCAL DAY. Every id comes from the one fixed-seed Random, and
/// every timestamp is built from the club-local date of <c>now</c> rather than from <c>now</c> itself, so
/// a reseed on the same day produces identical rows. The Random is consumed in one fixed order - adding
/// a draw anywhere changes everything after it, which is fine, but do it knowingly.
/// </para>
///
/// <para>
/// BOOKINGS MIRROR BookingProtocol, BECAUSE NOTHING ELSE WILL. The seeder writes through the DbContext,
/// and the database enforces only the one-active-booking-per-member index: capacity and the karnet gate
/// live in BookingProtocol alone. So every booking here passes the same three checks - the class has a
/// free spot, the member holds a pass covering the class's CLUB-LOCAL date (first by ValidFrom, as
/// FindCoveringAsync orders), and that pass has an entry left counted from active bookings - with both
/// counters tracked in memory as bookings accrue. A booking this class would refuse is a bug in it.
/// </para>
/// </summary>
public sealed class TestDataGenerator
{
    public const string EmailDomain = "example.test";

    /// <summary>The seeder's "already seeded" marker: present means the data set is in place.</summary>
    public const string SentinelEmail = "admin1@" + EmailDomain;

    public const int AccountMemberCount = 160;
    public const int AccountlessMemberCount = 40;

    /// <summary>Classes and passes span this many club-local days either side of today.</summary>
    public const int WindowDays = 28;

    private const int ActivePlanCount = 8;
    private const int UsedUpPassCount = 6;
    private const int FullClassCount = 3;

    /// <summary>
    /// The newest a member can be. Older than the oldest pass (an expired one issued up to about 61 days
    /// back) and the oldest class, so no member's pass or booking predates the member.
    /// </summary>
    private const int MemberMinAgeDays = 90;

    private static readonly (string Name, int Entries)[] PassTypes =
    [
        ("Karnet 8 wejść", 8),
        ("Karnet 12 wejść", 12),
        ("Karnet OPEN 30 wejść", 30),
    ];

    /// <summary>Club-local start times. Gaps of at least 60 minutes, so one instructor never overlaps.</summary>
    private static readonly TimeOnly[] Slots =
    [
        new(7, 0), new(8, 30), new(10, 0), new(12, 0), new(16, 0), new(17, 30), new(19, 0), new(20, 0),
    ];

    private static readonly string[] PhonePrefixes = ["5", "6", "7", "8"];
    private static readonly int[] HoldSeconds = [30, 45, 60];
    private static readonly string[] RepSchemes = ["8", "10", "12", "8-10", "10-12", "15"];
    private static readonly int[] RestSeconds = [60, 90, 120];

    private readonly Random _random;
    private readonly DateOnly _today;
    private readonly TimeZoneInfo _zone = ClubTime.Zone;

    /// <summary>
    /// Where booking generation splits "past" from "future": the start of TOMORROW, club-local, rather than
    /// <c>now</c>. A split on the instant would make the draw sequence depend on the time of day, and with
    /// it every booking, exercise and plan after it. Today's classes count as past, so the full and the
    /// cancelled classes always land from tomorrow on, whatever hour the seed runs.
    /// </summary>
    private readonly DateTimeOffset _endOfToday;

    private readonly List<SeedAccount> _accounts = [];
    private readonly List<Member> _members = [];
    private readonly List<MembershipPass> _passes = [];
    private readonly List<ClassType> _classTypes = [];
    private readonly List<Class> _classes = [];
    private readonly List<Booking> _bookings = [];
    private readonly List<Exercise> _exercises = [];
    private readonly List<TrainingPlan> _plans = [];

    private readonly List<Member> _admins = [];
    private readonly List<Member> _trainers = [];
    private readonly HashSet<Guid> _usedUpMembers = [];

    private readonly Dictionary<Guid, List<MembershipPass>> _passesByMember = [];
    private readonly Dictionary<Guid, int> _activeByClass = [];
    private readonly Dictionary<Guid, int> _activeByPass = [];
    private readonly HashSet<(Guid ClassId, Guid MemberId)> _booked = [];

    private TestDataGenerator(int seed, DateTimeOffset now)
    {
        _random = new Random(seed);
        _today = DateOnly.FromDateTime(ClubTime.ToClubLocal(now).DateTime);
        _endOfToday = At(1, 0, 0);
    }

    public static TestDataSet Generate(int seed, DateTimeOffset now) => new TestDataGenerator(seed, now).Build();

    private TestDataSet Build()
    {
        AddStaff();
        AddMembers();
        AddPasses();
        AddClassTypes();
        AddClasses();
        AddBookings();
        AddExercises();
        AddPlans();

        return new TestDataSet(
            _accounts, _members, _passes, _classTypes, _classes, _bookings, _exercises, _plans);
    }

    // ------------------------------------------------------------------
    // People
    // ------------------------------------------------------------------

    private void AddStaff()
    {
        for (var i = 1; i <= 2; i++)
        {
            _admins.Add(AddAccountMember($"admin{i}@{EmailDomain}", [ApplicationRoles.Admin], blocked: false));
        }

        for (var i = 1; i <= 2; i++)
        {
            _trainers.Add(AddAccountMember(
                $"trener{i}@{EmailDomain}", [ApplicationRoles.User, ApplicationRoles.Trainer], blocked: false));
        }
    }

    private void AddMembers()
    {
        // Blocked people are drawn up front, so the draw does not depend on anything generated later.
        var blockedAccounts = PickDistinct(Enumerable.Range(1, AccountMemberCount).ToList(), 4).ToHashSet();
        var blockedAccountless = _random.Next(1, AccountlessMemberCount + 1);

        for (var i = 1; i <= AccountMemberCount; i++)
        {
            AddAccountMember($"czlonek{i:000}@{EmailDomain}", [ApplicationRoles.User], blockedAccounts.Contains(i));
        }

        var codes = new HashSet<string>();

        for (var i = 1; i <= AccountlessMemberCount; i++)
        {
            var member = NewMember(
                userId: null,
                // A quarter carry no e-mail at all - the club recorded only a name and a phone.
                email: i % 4 == 0 ? null : $"bezkonta{i:00}@{EmailDomain}",
                blocked: i == blockedAccountless,
                createdAt: At(-_random.Next(MemberMinAgeDays, 300), 11, 0));

            // Every other one holds a live code, as if the admin had just printed an invitation.
            if (i % 2 == 1 && member.Status == MembershipStatus.Active)
            {
                member.AccessCode = NewAccessCode(codes);
                member.AccessCodeExpiresAt = At(_random.Next(1, (int)MemberAccessCode.Validity.TotalDays), 20, 0);
            }

            _members.Add(member);
        }
    }

    private Member AddAccountMember(string email, string[] roles, bool blocked)
    {
        var createdAt = At(-_random.Next(MemberMinAgeDays, 400), 12, 0);

        var user = new ApplicationUser
        {
            Id = NewId().ToString(),
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            Status = blocked ? AccountStatus.Blocked : AccountStatus.Active,
            CreatedAt = createdAt,
        };

        var member = NewMember(user.Id, email, blocked, createdAt);
        member.ClaimedAt = createdAt;
        user.DisplayName = member.DisplayName;

        _accounts.Add(new SeedAccount(user, roles));
        _members.Add(member);
        return member;
    }

    private Member NewMember(string? userId, string? email, bool blocked, DateTimeOffset createdAt)
    {
        var female = _random.Next(2) == 0;
        var first = Pick(female ? TestDataNames.FemaleFirstNames : TestDataNames.MaleFirstNames);
        var surname = Pick(TestDataNames.Surnames);

        var member = new Member
        {
            Id = NewId(),
            UserId = userId,
            DisplayName = $"{first} {(female ? surname.Female : surname.Male)}",
            Email = email,

            // Stored the way ContactDetails normalises it: nine digits, no prefix, no spaces.
            PhoneNumber = $"{Pick(PhonePrefixes)}{_random.Next(10_000_000, 100_000_000)}",
            Status = blocked ? MembershipStatus.Blocked : MembershipStatus.Active,
            CreatedAt = createdAt,
            ConcurrencyStamp = NewId().ToString(),
        };

        if (_random.Next(100) < 60)
        {
            var city = Pick(TestDataNames.Cities);
            member.Street = Pick(TestDataNames.Streets);
            member.HouseNumber = _random.Next(3) == 0
                ? $"{_random.Next(1, 120)}/{_random.Next(1, 40)}"
                : $"{_random.Next(1, 120)}";
            member.PostalCode = city.PostalCode;
            member.City = city.City;
        }

        return member;
    }

    private string NewAccessCode(HashSet<string> taken)
    {
        // The code is a credential, so it comes from MemberAccessCode.Generate's CSPRNG rather than the
        // seeded Random - a fixed seed in the repo would let anyone compute every live code on Staging.
        // The seeded Random is still advanced once per character, outside the retry loop, so every id
        // drawn after this stays exactly what it was.
        for (var i = 0; i < MemberAccessCode.Length; i++)
        {
            _random.Next();
        }

        while (true)
        {
            var code = MemberAccessCode.Generate();
            if (taken.Add(code))
            {
                return code;
            }
        }
    }

    // ------------------------------------------------------------------
    // Passes
    // ------------------------------------------------------------------

    private void AddPasses()
    {
        var clubMembers = ClubMembers().ToList();

        // Chosen first, and only among active members: their single pass is spent entirely on past
        // classes in AddBookings, which is what "used up" means when entries are derived.
        foreach (var member in PickDistinct(clubMembers.Where(m => m.Status == MembershipStatus.Active).ToList(), UsedUpPassCount))
        {
            _usedUpMembers.Add(member.Id);
        }

        foreach (var member in clubMembers)
        {
            if (_usedUpMembers.Contains(member.Id))
            {
                var from = _today.AddDays(-(WindowDays - 1));
                AddPass(member, PassTypes[0], from);
                continue;
            }

            var roll = _random.Next(100);

            if (roll < 8)
            {
                // No pass at all: bookable by nobody, refused with "no valid pass".
                continue;
            }

            if (roll < 22)
            {
                // Expired only.
                var to = _today.AddDays(-_random.Next(1, 21));
                AddPass(member, Pick(PassTypes), to.AddDays(-29));
                continue;
            }

            var currentFrom = _today.AddDays(-_random.Next(0, 26));

            if (roll < 47)
            {
                // An expired pass right before the current one - never overlapping, as the issue rule demands.
                var previousTo = currentFrom.AddDays(-1 - _random.Next(0, 4));
                AddPass(member, Pick(PassTypes), previousTo.AddDays(-29));
            }

            var current = AddPass(member, Pick(PassTypes), currentFrom);

            if (current.ValidTo < _today.AddDays(WindowDays) && _random.Next(100) < 30)
            {
                // Renewed ahead of time, so bookings further out still find a covering pass.
                AddPass(member, Pick(PassTypes), current.ValidTo.AddDays(1));
            }
        }
    }

    private MembershipPass AddPass(Member member, (string Name, int Entries) type, DateOnly validFrom)
    {
        var pass = new MembershipPass
        {
            Id = NewId(),
            MemberId = member.Id,
            TypeName = type.Name,
            ValidFrom = validFrom,
            ValidTo = validFrom.AddDays(29),
            EntryCount = type.Entries,
            IssuedAt = At(Math.Min(validFrom.DayNumber - _today.DayNumber - _random.Next(0, 4), -1), 10, 30),
            ConcurrencyStamp = NewId().ToString(),
        };

        _passes.Add(pass);

        if (!_passesByMember.TryGetValue(member.Id, out var list))
        {
            _passesByMember[member.Id] = list = [];
        }

        list.Add(pass);
        return pass;
    }

    // ------------------------------------------------------------------
    // Schedule
    // ------------------------------------------------------------------

    private void AddClassTypes()
    {
        foreach (var spec in TestDataNames.ClassTypes)
        {
            _classTypes.Add(new ClassType
            {
                Id = NewId(),
                Name = spec.Name,
                Description = spec.Description,
                DefaultDurationMinutes = spec.DurationMinutes,
                DefaultCapacity = spec.Capacity,
                IsActive = spec.IsActive,
                CreatedAt = At(-120, 9, 0),
            });
        }
    }

    private void AddClasses()
    {
        var activeTypes = _classTypes.Where(t => t.IsActive).ToList();

        for (var day = -WindowDays; day <= WindowDays; day++)
        {
            if (_today.AddDays(day).DayOfWeek == DayOfWeek.Sunday)
            {
                continue;
            }

            var slots = PickDistinct(Slots, _random.Next(3, 6)).Order().ToList();

            foreach (var slot in slots)
            {
                var type = Pick(activeTypes);

                _classes.Add(new Class
                {
                    Id = NewId(),
                    StartsAt = At(day, slot.Hour, slot.Minute),
                    DurationMinutes = type.DefaultDurationMinutes,
                    Capacity = type.DefaultCapacity,
                    Status = ClassStatus.Scheduled,
                    CreatedAt = At(Math.Min(day - 21, -1), 9, 0),
                    ConcurrencyStamp = NewId().ToString(),
                    ClassTypeId = type.Id,
                    InstructorMemberId = Pick(_trainers).Id,
                });
            }
        }
    }

    // ------------------------------------------------------------------
    // Bookings - see the class comment: these mirror BookingProtocol's checks exactly.
    // ------------------------------------------------------------------

    private void AddBookings()
    {
        var bookable = ClubMembers().Where(m => m.Status == MembershipStatus.Active).ToList();
        var past = _classes.Where(c => c.StartsAt <= _endOfToday).ToList();
        var future = _classes.Where(c => c.StartsAt > _endOfToday).ToList();

        // 1. Used-up passes: every entry spent on past classes inside the pass's range.
        // Walked in member order, not HashSet order, so the draw sequence cannot depend on hashing.
        foreach (var memberId in _members.Where(m => _usedUpMembers.Contains(m.Id)).Select(m => m.Id))
        {
            var pass = _passesByMember[memberId].Single();

            foreach (var cls in Shuffle(past.Where(c => Covers(pass, c)).ToList()))
            {
                if (UsedEntries(pass) >= pass.EntryCount)
                {
                    break;
                }

                TryBook(cls, memberId);
            }

            if (UsedEntries(pass) < pass.EntryCount)
            {
                throw new InvalidOperationException("Test data: could not use up a pass on past classes.");
            }
        }

        // 2. Classes filled exactly to capacity, in the next few days, where every current pass covers.
        var nearFuture = future
            .Where(c => ClubDate(c) <= _today.AddDays(4))
            .OrderBy(c => c.Capacity)
            .ToList();

        var full = nearFuture.DistinctBy(ClubDate).Take(FullClassCount).ToList();
        var fullIds = full.Select(c => c.Id).ToHashSet();

        foreach (var cls in full)
        {
            foreach (var member in Shuffle(bookable.Where(m => !_usedUpMembers.Contains(m.Id)).ToList()))
            {
                if (Booked(cls) >= cls.Capacity)
                {
                    break;
                }

                TryBook(cls, member.Id);
            }

            if (Booked(cls) != cls.Capacity)
            {
                throw new InvalidOperationException("Test data: could not fill a class to capacity.");
            }
        }

        // 3. Everything else, partly booked: fuller in the past and the coming week, thinner further out.
        foreach (var cls in _classes.Where(c => !fullIds.Contains(c.Id)).OrderBy(c => c.StartsAt))
        {
            var daysAhead = ClubDate(cls).DayNumber - _today.DayNumber;
            var (min, max) = cls.StartsAt <= _endOfToday ? (30, 80) : daysAhead <= 7 ? (20, 70) : (0, 35);
            var target = cls.Capacity * _random.Next(min, max + 1) / 100;

            foreach (var member in Shuffle(bookable.ToList()))
            {
                if (Booked(cls) >= target)
                {
                    break;
                }

                TryBook(cls, member.Id);
            }
        }

        // 4. Two future classes cancelled by the club. Their bookings stay ACTIVE: CancelClass touches
        //    only the class's status, because cancelling the bookings would record that the MEMBERS
        //    cancelled. Picked last so they carry bookings, and never one of the full classes.
        foreach (var cls in PickDistinct(future.Where(c => !fullIds.Contains(c.Id) && Booked(c) > 0).ToList(), 2))
        {
            cls.Status = ClassStatus.Cancelled;
        }
    }

    private void TryBook(Class cls, Guid memberId)
    {
        if (_booked.Contains((cls.Id, memberId)) || Booked(cls) >= cls.Capacity)
        {
            return;
        }

        var date = ClubDate(cls);
        var pass = _passesByMember.TryGetValue(memberId, out var passes)
            ? passes.Where(p => p.ValidFrom <= date && date <= p.ValidTo).OrderBy(p => p.ValidFrom).FirstOrDefault()
            : null;

        if (pass is null || UsedEntries(pass) >= pass.EntryCount)
        {
            return;
        }

        _bookings.Add(new Booking
        {
            Id = NewId(),
            ClassId = cls.Id,
            MemberId = memberId,
            MembershipPassId = pass.Id,
            Status = BookingStatus.Active,
            CreatedAt = cls.StartsAt <= _endOfToday
                ? cls.StartsAt.AddHours(-_random.Next(2, 145))
                : At(-_random.Next(1, 8), _random.Next(8, 22), 0),
        });

        _booked.Add((cls.Id, memberId));
        _activeByClass[cls.Id] = Booked(cls) + 1;
        _activeByPass[pass.Id] = UsedEntries(pass) + 1;
    }

    private int Booked(Class cls) => _activeByClass.GetValueOrDefault(cls.Id);

    private int UsedEntries(MembershipPass pass) => _activeByPass.GetValueOrDefault(pass.Id);

    private static DateOnly ClubDate(Class cls) => DateOnly.FromDateTime(ClubTime.ToClubLocal(cls.StartsAt).DateTime);

    private static bool Covers(MembershipPass pass, Class cls) =>
        pass.ValidFrom <= ClubDate(cls) && ClubDate(cls) <= pass.ValidTo;

    // ------------------------------------------------------------------
    // Training
    // ------------------------------------------------------------------

    private void AddExercises()
    {
        var videoIds = TestDataNames.VideoUrls
            .Select(url => YouTubeVideoId.TryParse(url, out var id)
                ? id
                : throw new InvalidOperationException($"Test data: '{url}' is not a parseable YouTube URL."))
            .ToArray();

        foreach (var spec in TestDataNames.Exercises)
        {
            _exercises.Add(new Exercise
            {
                Id = NewId(),
                Name = spec.Name,
                Description = spec.Description,
                MuscleGroup = spec.MuscleGroup,
                Difficulty = spec.Difficulty,
                Equipment = spec.Equipment,
                Preparation = spec.Preparation,
                StartingPosition = spec.StartingPosition,
                Execution = spec.Execution,
                VideoId = Pick(videoIds),
                IsActive = spec.IsActive,
                CreatedAt = At(-90, 9, 0),
            });
        }
    }

    private void AddPlans()
    {
        var active = _members.Where(m => m.Status == MembershipStatus.Active).ToList();
        var withAccount = active.Where(m => m.UserId is not null && !_admins.Contains(m) && !_trainers.Contains(m)).ToList();
        var accountless = active.Where(m => m.UserId is null).ToList();

        var owners = PickDistinct(withAccount, ActivePlanCount - 2).Concat(PickDistinct(accountless, 2)).ToList();
        var assigners = _trainers.Concat(_admins).ToList();

        for (var i = 0; i < owners.Count; i++)
        {
            var createdAt = At(-_random.Next(3, 40), 10, 0);

            // The first two owners also carry an archived predecessor, so a member's plan history
            // exists and the one-ACTIVE-plan index is exercised rather than trivially satisfied.
            if (i < 2)
            {
                var archivedAt = createdAt.AddMinutes(-5);
                _plans.Add(NewPlan(
                    owners[i], Pick(assigners), $"{TestDataNames.PlanNames[i]} (poprzedni)",
                    TrainingPlanStatus.Archived, archivedAt.AddDays(-_random.Next(20, 60)), archivedAt));
            }

            _plans.Add(NewPlan(
                owners[i], Pick(assigners), TestDataNames.PlanNames[i], TrainingPlanStatus.Active, createdAt, null));
        }
    }

    private TrainingPlan NewPlan(
        Member owner,
        Member assignedBy,
        string name,
        TrainingPlanStatus status,
        DateTimeOffset createdAt,
        DateTimeOffset? archivedAt)
    {
        var plan = new TrainingPlan
        {
            Id = NewId(),
            Name = name,
            MemberId = owner.Id,
            AssignedByMemberId = assignedBy.Id,
            Status = status,
            CreatedAt = createdAt,
            ArchivedAt = archivedAt,
            ConcurrencyStamp = NewId().ToString(),
        };

        var exercises = PickDistinct(_exercises.Where(e => e.IsActive).ToList(), _random.Next(4, 9));

        for (var position = 0; position < exercises.Count; position++)
        {
            var exercise = exercises[position];
            var item = new TrainingPlanItem
            {
                Id = NewId(),
                TrainingPlanId = plan.Id,
                ExerciseId = exercise.Id,
                Position = position,
            };

            if (exercise.MuscleGroup == "Mobilność" || exercise.Name == "Deska")
            {
                item.Sets = _random.Next(2, 4);
                item.DurationSeconds = Pick(HoldSeconds);
                item.RestSeconds = 30;
            }
            else
            {
                item.Sets = _random.Next(3, 5);
                item.Reps = Pick(RepSchemes);
                item.RestSeconds = Pick(RestSeconds);

                if (exercise.Equipment is { } equipment
                    && (equipment.Contains("Sztanga") || equipment.Contains("Hantl") || equipment.Contains("Suwnica")))
                {
                    item.WeightKg = _random.Next(2, 33) * 2.5m;
                }
            }

            if (_random.Next(100) < 30)
            {
                item.Note = Pick(TestDataNames.ItemNotes);
            }

            plan.Items.Add(item);
        }

        return plan;
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    /// <summary>Everyone but the four staff accounts - the 200 people the club serves.</summary>
    private IEnumerable<Member> ClubMembers() =>
        _members.Where(m => !_admins.Contains(m) && !_trainers.Contains(m));

    /// <summary>
    /// A club-local wall-clock time <paramref name="dayOffset"/> days from today, as a UTC instant.
    /// Goes through ClubTime.AddLocalDays so an 18:00 class stays 18:00 across the October DST change
    /// the window straddles. Today's anchor itself is safe: seeded times are 07:00-21:00, nowhere near
    /// the 02:00-03:00 transition hour.
    /// </summary>
    private DateTimeOffset At(int dayOffset, int hour, int minute)
    {
        var local = _today.ToDateTime(new TimeOnly(hour, minute));
        var anchor = new DateTimeOffset(local, _zone.GetUtcOffset(local));
        return ClubTime.AddLocalDays(anchor, dayOffset);
    }

    private Guid NewId()
    {
        var bytes = new byte[16];
        _random.NextBytes(bytes);

        // Stamp the RFC 4122 version-4 and variant bits, so the ids look like every other Guid in the app.
        bytes[7] = (byte)((bytes[7] & 0x0F) | 0x40);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        return new Guid(bytes);
    }

    private T Pick<T>(IReadOnlyList<T> items) => items[_random.Next(items.Count)];

    private List<T> Shuffle<T>(List<T> items)
    {
        for (var i = items.Count - 1; i > 0; i--)
        {
            var j = _random.Next(i + 1);
            (items[i], items[j]) = (items[j], items[i]);
        }

        return items;
    }

    private List<T> PickDistinct<T>(IReadOnlyList<T> items, int count) =>
        Shuffle(items.ToList()).Take(count).ToList();
}
