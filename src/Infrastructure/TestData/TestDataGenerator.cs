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
    IReadOnlyList<ClassGroup> ClassGroups,
    IReadOnlyList<Class> Classes,
    IReadOnlyList<Booking> Bookings,
    IReadOnlyList<Exercise> Exercises,
    IReadOnlyList<TrainingPlan> Plans,
    IReadOnlyList<GroupRosterEntry> RosterEntries);

/// <summary>
/// Builds the test club (S-24) in memory, shaped on the club's own spreadsheets (club-shaped-test-data):
/// fixed weekly groups of one to six people with a roster entered once (treningi.xlsx), monthly karnety,
/// one-off entries and vouchers with their payment states (twt.xlsx), attendance and makeups. Knows
/// nothing about the database: <see cref="TestDataSeeder"/> persists what this returns.
///
/// <para>
/// DETERMINISTIC FOR A GIVEN SEED AND CLUB-LOCAL DAY. Every id comes from the one fixed-seed Random, and
/// every timestamp is built from the club-local date of <c>now</c> rather than from <c>now</c> itself, so
/// a reseed on the same day produces identical rows. The Random is consumed in one fixed order - adding
/// a draw anywhere changes everything after it, which is fine, but do it knowingly.
/// </para>
///
/// <para>
/// EVERY ROW IS ONE THE APP COULD HAVE WRITTEN, BECAUSE NOTHING ELSE WILL CHECK. The seeder writes
/// through the DbContext, and the database enforces only two unique indexes. So every write here passes
/// the app's own rules, mirrored in memory: classes never overlap club-wide (HasTimeConflictAsync); a
/// booking needs a free spot, a karnet covering the class's CLUB-LOCAL date (first by ValidFrom) and an
/// entry left by EntryConsumption's count (BookingProtocol); attendance only on a class that has started
/// (RecordAttendance); a makeup within MakeupRules.DeadlineDays, on a class with a free spot, under a
/// karnet covering its own date, spending no entry (BookMakeup). A row the app would refuse is a bug
/// here, and TestDataGeneratorTests exists to catch it.
/// </para>
///
/// <para>
/// GUARANTEES ARE BUILT, NOT DRAWN. "Some karnety end within five days", "some are unpaid", "every makeup
/// state exists" must hold whatever day the seed runs, so those cases are placed relative to today rather
/// than left to the dice.
/// </para>
/// </summary>
public sealed class TestDataGenerator
{
    public const string EmailDomain = "example.test";

    /// <summary>The seeder's "already seeded" marker: present means the data set is in place.</summary>
    public const string SentinelEmail = "admin1@" + EmailDomain;

    public const int AccountMemberCount = 70;
    public const int AccountlessMemberCount = 30;
    public const int TrainerCount = 4;

    /// <summary>
    /// Classes span this many club-local days back. Longer than <see cref="MakeupRules.DeadlineDays"/>, so
    /// an absence can be old enough for its makeup deadline to have passed.
    /// </summary>
    public const int PastDays = 35;

    /// <summary>Classes span this many club-local days ahead.</summary>
    public const int FutureDays = 28;

    /// <summary>Karnety covering today that end within the "Kończą się karnety" five-day window.</summary>
    public const int ExpiringCount = 8;

    public const int UnpaidCurrentCount = 5;
    public const int UnpaidExpiredCount = 3;

    /// <summary>Roster members whose karnet ran out with no renewal - their future classes are gaps.</summary>
    public const int LapsedCount = 4;

    private const int RecentJoinerCount = 3;
    private const int BlockedAccountCount = 3;
    private const int ActivePlanCount = 8;

    /// <summary>The account the manual test scripts sign in as; always an active roster member.</summary>
    public const string ReferenceMemberEmail = "czlonek010@" + EmailDomain;

    /// <summary>
    /// The newest a member can be. Older than the oldest karnet (a chain reaching back past the window)
    /// and the oldest roster entry, so nothing a member holds predates them.
    /// </summary>
    private const int MemberMinAgeDays = 90;

    private static readonly string[] PhonePrefixes = ["5", "6", "7", "8"];
    private static readonly int[] HoldSeconds = [30, 45, 60];
    private static readonly string[] RepSchemes = ["8", "10", "12", "8-10", "10-12", "15"];
    private static readonly int[] RestSeconds = [60, 90, 120];

    private readonly Random _random;

    private readonly DateOnly _today;
    private readonly TimeZoneInfo _zone = ClubTime.Zone;

    /// <summary>
    /// Today's club-local midnight: where generation splits "past" from "today on", and the latest that
    /// anything recorded by staff is dated. A split on <c>now</c> itself would make the draw sequence
    /// depend on the time of day, and with it every row after it.
    /// </summary>
    private readonly DateTimeOffset _startOfToday;

    private readonly List<SeedAccount> _accounts = [];
    private readonly List<Member> _members = [];
    private readonly List<MembershipPass> _passes = [];
    private readonly List<ClassGroup> _classGroups = [];
    private readonly List<Class> _classes = [];
    private readonly List<Booking> _bookings = [];
    private readonly List<Exercise> _exercises = [];
    private readonly List<TrainingPlan> _plans = [];
    private readonly List<GroupRosterEntry> _rosterEntries = [];

    private readonly List<Member> _admins = [];
    private readonly List<Member> _trainers = [];
    private readonly List<GroupPlan> _groups = [];

    private readonly List<Member> _formerClients = [];
    private readonly List<Member> _oneOffHolders = [];
    private readonly List<Member> _voucherHolders = [];

    private readonly Dictionary<Guid, List<MembershipPass>> _passesByMember = [];
    private readonly Dictionary<Guid, Member> _memberById = [];
    private readonly Dictionary<Guid, GroupPlan> _groupById = [];
    private readonly Dictionary<Guid, int> _activeByClass = [];
    private readonly Dictionary<Guid, int> _activeByPass = [];
    private readonly HashSet<(Guid ClassId, Guid MemberId)> _booked = [];

    /// <summary>One fixed weekly group as the generator plans it, before and after it becomes rows.</summary>
    private sealed class GroupPlan
    {
        public required ClassGroup Group { get; init; }
        public required TestDataNames.GroupKindSpec Kind { get; init; }
        public required DayOfWeek Day { get; init; }
        public required string DayShort { get; init; }
        public required TimeOnly Time { get; init; }
        public required Member Instructor { get; init; }
        public int Target { get; init; }
        public List<(Member Member, DateTimeOffset AddedAt)> Roster { get; } = [];
    }

    private TestDataGenerator(int seed, DateTimeOffset now)
    {
        _random = new Random(seed);
        _today = DateOnly.FromDateTime(ClubTime.ToClubLocal(now).DateTime);
        _startOfToday = At(0, 0, 0);
    }

    public static TestDataSet Generate(int seed, DateTimeOffset now) => new TestDataGenerator(seed, now).Build();

    private TestDataSet Build()
    {
        AddStaff();
        AddMembers();
        AddGroupsAndRosters();
        AddPasses();
        AddClasses();
        AddBookings();
        CancelTwoClasses();
        AddAttendanceAndMakeups();
        AddExercises();
        AddPlans();

        return new TestDataSet(
            _accounts, _members, _passes, _classGroups, _classes, _bookings, _exercises, _plans, _rosterEntries);
    }

    // ------------------------------------------------------------------
    // People
    // ------------------------------------------------------------------

    private void AddStaff()
    {
        // admin2 also teaches (S-25): an Admin+Trainer instructing a few groups, so the staff dashboard and
        // the Admin-over-Trainer precedence are visible on Staging. admin1 stays the admin who teaches
        // nothing, whose "Twoje zajęcia" is empty.
        for (var i = 1; i <= 2; i++)
        {
            string[] roles = i == 2 ? [ApplicationRoles.Admin, ApplicationRoles.Trainer] : [ApplicationRoles.Admin];
            _admins.Add(AddAccountMember($"admin{i}@{EmailDomain}", roles, blocked: false));
        }

        // Fictional names, like everyone else here: the spreadsheet's real trainers stay out of seed data.
        for (var i = 1; i <= TrainerCount; i++)
        {
            _trainers.Add(AddAccountMember(
                $"trener{i}@{EmailDomain}", [ApplicationRoles.User, ApplicationRoles.Trainer], blocked: false));
        }
    }

    private void AddMembers()
    {
        // Blocked people are drawn up front, so the draw does not depend on anything generated later -
        // and never among the first twenty, so czlonek010 (the manual scripts' member) stays active.
        var blockedAccounts = PickDistinct(Enumerable.Range(21, AccountMemberCount - 20).ToList(), BlockedAccountCount)
            .ToHashSet();
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

            AddMember(member);
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
        AddMember(member);
        return member;
    }

    private void AddMember(Member member)
    {
        _members.Add(member);
        _memberById[member.Id] = member;
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
    // Groups and rosters
    // ------------------------------------------------------------------

    private void AddGroupsAndRosters()
    {
        // The weekly slot table: five slots a day, Monday to Saturday, drawn from SlotTimes - whose
        // spacing makes ANY subset overlap-free. One group per slot.
        var slots = new List<(DayOfWeek Day, string Short, TimeOnly Time)>();
        foreach (var (day, dayShort) in TestDataNames.TrainingDays)
        {
            slots.AddRange(PickDistinct(TestDataNames.SlotTimes, 5).Order().Select(time => (day, dayShort, time)));
        }

        var kinds = TestDataNames.GroupKinds.SelectMany(kind => Enumerable.Repeat(kind, kind.Count)).ToList();
        if (kinds.Count != slots.Count)
        {
            throw new InvalidOperationException("Test data: the group mix does not fill the weekly slot table.");
        }

        kinds = Shuffle(kinds);

        // admin2 teaches three groups; the four trainers share the rest.
        var instructors = Enumerable.Range(0, slots.Count)
            .Select(i => i < 3 ? _admins[1] : _trainers[i % _trainers.Count])
            .ToList();
        instructors = Shuffle(instructors);

        for (var i = 0; i < slots.Count; i++)
        {
            var kind = kinds[i];
            _groups.Add(new GroupPlan
            {
                Group = new ClassGroup { Id = NewId() },
                Kind = kind,
                Day = slots[i].Day,
                DayShort = slots[i].Short,
                Time = slots[i].Time,
                Instructor = instructors[i],
                // Most rosters are full; a group of six holds four to six, which is where one-offs,
                // vouchers and makeups find their free spots.
                Target = kind.Capacity == 6 ? _random.Next(4, 7) : kind.Capacity,
            });
        }

        FillRosters();

        foreach (var plan in _groups)
        {
            var name = plan.Kind.NamePrefix is null
                ? $"{plan.Roster[0].Member.DisplayName} – indywidualny"
                : $"{plan.Kind.NamePrefix} {plan.DayShort} {plan.Time:HH\\:mm}";

            AddGroupRows(plan, name, isActive: true);
        }

        AddFinishedGroup(slots);
    }

    private void FillRosters()
    {
        var reference = _members.Single(m => m.Email == ReferenceMemberEmail);
        var active = ClubMembers().Where(m => m.Status == MembershipStatus.Active && m != reference).ToList();
        var pool = new Queue<Member>([reference, .. Shuffle(active)]);

        foreach (var plan in _groups)
        {
            while (plan.Roster.Count < plan.Target)
            {
                // Entered long before the window, as a fixed roster is: once.
                plan.Roster.Add((pool.Dequeue(), At(-_random.Next(40, 61), 9, 0)));
            }
        }

        // The last few filled joined only recently: their bookings start from the day they were added.
        var joiners = _groups.SelectMany(g => g.Roster.Select(r => (Group: g, r.Member)))
            .Where(x => x.Member != reference)
            .TakeLast(RecentJoinerCount)
            .ToList();

        for (var i = 0; i < joiners.Count; i++)
        {
            var roster = joiners[i].Group.Roster;
            var at = roster.FindIndex(r => r.Member == joiners[i].Member);
            roster[at] = (joiners[i].Member, At(-(3 + 3 * i), 9, 0));
        }

        // One blocked member stays in a roster: blocking does not remove anyone (S-37), and the roster
        // screen then shows a member_blocked gap.
        var blocked = ClubMembers().First(m => m.Status == MembershipStatus.Blocked && m.UserId is not null);
        _groups.First(g => g.Kind.Capacity == 6 && g.Roster.Count < g.Kind.Capacity)
            .Roster.Add((blocked, At(-50, 9, 0)));

        // Whoever is left is not in a fixed group: former clients, one-off and voucher holders, and a
        // few with no karnet at all. Three former clients are kept for the finished group's history.
        var rest = pool.ToList();
        for (var i = 0; i < rest.Count; i++)
        {
            (i < 3 ? _formerClients : (i % 4) switch
            {
                0 => _formerClients,
                1 => _oneOffHolders,
                2 => _voucherHolders,
                _ => null,
            })?.Add(rest[i]);
        }

        // The blocked members outside any roster are former clients too.
        _formerClients.AddRange(ClubMembers().Where(m => m.Status == MembershipStatus.Blocked && m != blocked));
    }

    /// <summary>
    /// A group of six that stopped training two weeks ago: inactive, past classes only, its roster of
    /// three former clients kept - the admin's list shows both states, and the history reads.
    /// </summary>
    private void AddFinishedGroup(List<(DayOfWeek Day, string Short, TimeOnly Time)> slots)
    {
        var (day, dayShort) = TestDataNames.TrainingDays[^1];
        var time = TestDataNames.SlotTimes.First(t => !slots.Any(s => s.Day == day && s.Time == t));
        var kind = TestDataNames.GroupKinds[^1];

        var plan = new GroupPlan
        {
            Group = new ClassGroup { Id = NewId() },
            Kind = kind,
            Day = day,
            DayShort = dayShort,
            Time = time,
            Instructor = _trainers[0],
        };

        foreach (var member in _formerClients.Take(3))
        {
            plan.Roster.Add((member, At(-80, 9, 0)));
        }

        _groups.Add(plan);
        AddGroupRows(plan, $"{kind.NamePrefix} {dayShort} {time:HH\\:mm} (zakończona)", isActive: false);
    }

    private void AddGroupRows(GroupPlan plan, string name, bool isActive)
    {
        var group = plan.Group;
        group.Name = name;
        group.Description = plan.Kind.Description;
        group.DefaultDurationMinutes = plan.Kind.DurationMinutes;
        group.DefaultCapacity = plan.Kind.Capacity;
        group.IsActive = isActive;
        group.CreatedAt = At(-120, 9, 0);

        _classGroups.Add(group);
        _groupById[group.Id] = plan;

        foreach (var (member, addedAt) in plan.Roster)
        {
            _rosterEntries.Add(new GroupRosterEntry
            {
                Id = NewId(),
                ClassGroupId = group.Id,
                MemberId = member.Id,
                AddedAt = addedAt,
                AddedBy = _admins[0].UserId,
            });
        }
    }

    // ------------------------------------------------------------------
    // Karnety
    // ------------------------------------------------------------------

    private void AddPasses()
    {
        var reference = _members.Single(m => m.Email == ReferenceMemberEmail);
        var active = _groups.Where(g => g.Group.IsActive).ToList();

        // Joiners carry a single karnet from the day they joined; everyone else in a roster a monthly chain.
        var joinerIds = active.SelectMany(g => g.Roster)
            .Where(r => r.AddedAt > At(-PastDays, 0, 0))
            .Select(r => r.Member.Id)
            .ToHashSet();

        var regulars = active.SelectMany(g => g.Roster.Select(r => r.Member))
            .Where(m => !joinerIds.Contains(m.Id))
            .ToList();

        // The guaranteed cases go to members other than the reference one, which keeps a plain live karnet.
        var cased = Shuffle(regulars.Where(m => m != reference && m.Status == MembershipStatus.Active).ToList());
        var expiring = cased.Take(ExpiringCount).ToList();
        var lapsed = cased.Skip(ExpiringCount).Take(LapsedCount).ToList();

        foreach (var member in regulars)
        {
            DateOnly currentFrom;
            var renewable = true;

            if (expiring.IndexOf(member) is var e and >= 0)
            {
                // Ends today to five days from now, not renewed: "Kończą się karnety" lists them.
                currentFrom = _today.AddDays(e % 6 - 29);
                renewable = false;
            }
            else if (lapsed.IndexOf(member) is var l and >= 0)
            {
                // Ran out one to ten days ago and nobody renewed it.
                currentFrom = _today.AddDays(-1 - (3 * l) % 10 - 29);
                renewable = false;
            }
            else
            {
                currentFrom = _today.AddDays(-_random.Next(0, 30));
            }

            AddMonthlyChain(member, currentFrom, renewable);
        }

        foreach (var member in active.SelectMany(g => g.Roster).Where(r => joinerIds.Contains(r.Member.Id)))
        {
            AddPass(member.Member, TestDataNames.Monthly, DateOnly.FromDateTime(ClubTime.ToClubLocal(member.AddedAt).DateTime));
        }

        // The finished group's former clients trained until two weeks ago; the other former clients
        // simply let their last karnet run out.
        for (var i = 0; i < _formerClients.Count; i++)
        {
            var to = i < 3 ? _today.AddDays(-11) : _today.AddDays(-_random.Next(3, 31));
            AddPass(_formerClients[i], TestDataNames.Monthly, to.AddDays(-29));
        }

        foreach (var member in _oneOffHolders)
        {
            AddPass(member, TestDataNames.OneOff, _today.AddDays(-_random.Next(0, 13)));
        }

        foreach (var member in _voucherHolders)
        {
            AddPass(member, TestDataNames.Voucher, _today.AddDays(-_random.Next(0, 21)));
        }

        MarkUnpaid();
    }

    /// <summary>
    /// The member's karnet starting <paramref name="currentFrom"/>, its predecessors back past the window -
    /// mostly back to back, sometimes renewed a few days late - and, for about a third, its successor bought
    /// ahead. Never overlapping, as the issue rule demands.
    /// </summary>
    private void AddMonthlyChain(Member member, DateOnly currentFrom, bool renewable)
    {
        var chain = new List<DateOnly> { currentFrom };
        var from = currentFrom;

        while (from > _today.AddDays(-PastDays))
        {
            var previousTo = from.AddDays(-1 - (_random.Next(100) < 15 ? _random.Next(1, 5) : 0));
            from = previousTo.AddDays(-(TestDataNames.Monthly.ValidityDays - 1));
            chain.Insert(0, from);
        }

        var currentTo = currentFrom.AddDays(TestDataNames.Monthly.ValidityDays - 1);
        if (renewable && currentTo < _today.AddDays(FutureDays) && _random.Next(100) < 35)
        {
            chain.Add(currentTo.AddDays(1));
        }

        foreach (var validFrom in chain)
        {
            AddPass(member, TestDataNames.Monthly, validFrom);
        }
    }

    private MembershipPass AddPass(Member member, TestDataNames.PassTypeSpec type, DateOnly validFrom)
    {
        var pass = new MembershipPass
        {
            Id = NewId(),
            MemberId = member.Id,
            TypeName = type.Name,
            ValidFrom = validFrom,
            ValidTo = validFrom.AddDays(type.ValidityDays - 1),
            EntryCount = type.Entries,
            // Always the day BEFORE it starts or earlier, and never after yesterday: a karnet's bookings are
            // dated after its issue, and its first class can be at 06:15.
            IssuedAt = At(Math.Min(validFrom.DayNumber - _today.DayNumber - 1 - _random.Next(0, 4), -1), 10, 30),
            // Paid on the first valid day - never later than today, since a payment is never dated in the
            // future. MarkUnpaid then takes the guaranteed few away. No recorder: nobody marked these in
            // the app.
            PaidAt = validFrom > _today ? _today : validFrom,
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

    /// <summary>"BRAK PŁATNOŚCI": a fixed number of current karnety and of expired ones, never paid.</summary>
    private void MarkUnpaid()
    {
        var current = Shuffle(_passes.Where(p => p.ValidFrom <= _today && _today <= p.ValidTo).ToList());
        foreach (var pass in current.Take(UnpaidCurrentCount))
        {
            pass.PaidAt = null;
        }

        var expired = Shuffle(_passes.Where(p => p.ValidTo < _today).ToList());
        foreach (var pass in expired.Take(UnpaidExpiredCount))
        {
            pass.PaidAt = null;
        }
    }

    // ------------------------------------------------------------------
    // Schedule
    // ------------------------------------------------------------------

    private void AddClasses()
    {
        foreach (var plan in _groups)
        {
            var last = plan.Group.IsActive ? FutureDays : -15;

            for (var day = -PastDays; day <= last; day++)
            {
                if (_today.AddDays(day).DayOfWeek != plan.Day)
                {
                    continue;
                }

                _classes.Add(new Class
                {
                    Id = NewId(),
                    StartsAt = At(day, plan.Time.Hour, plan.Time.Minute),
                    DurationMinutes = plan.Kind.DurationMinutes,
                    Capacity = plan.Kind.Capacity,
                    Status = ClassStatus.Scheduled,
                    CreatedAt = At(Math.Min(day - 21, -1), 9, 0),
                    ConcurrencyStamp = NewId().ToString(),
                    ClassGroupId = plan.Group.Id,
                    InstructorMemberId = plan.Instructor.Id,
                });
            }
        }

        var weekly = Shuffle(_classes.Where(c => _groupById[c.ClassGroupId].Group.IsActive).ToList());

        // Substitutions: another instructor on four occurrences - the substitute now sees the class as theirs.
        foreach (var cls in weekly.Take(4))
        {
            var instructors = Instructors;
            var at = instructors.FindIndex(m => m.Id == cls.InstructorMemberId);
            cls.InstructorMemberId = instructors[(at + 1) % instructors.Count].Id;
        }

        // Two occurrences moved to another time that day: a SlotTimes entry no other class of that weekday
        // uses, on two different dates, so the moved classes cannot collide with anything either.
        var movedDates = new HashSet<DateOnly>();
        foreach (var cls in weekly.Skip(4).Where(c => Math.Abs(ClubDate(c).DayNumber - _today.DayNumber) <= 20))
        {
            if (movedDates.Count == 2)
            {
                break;
            }

            var date = ClubDate(cls);
            var taken = _classes.Where(c => ClubDate(c) == date).Select(c => TimeOnly.FromDateTime(ClubTime.ToClubLocal(c.StartsAt).DateTime));
            var free = TestDataNames.SlotTimes.Except(taken).ToList();

            if (free.Count > 0 && movedDates.Add(date))
            {
                var time = free[_random.Next(free.Count)];
                cls.StartsAt = At(date.DayNumber - _today.DayNumber, time.Hour, time.Minute);
            }
        }
    }

    // ------------------------------------------------------------------
    // Bookings - see the class comment: these mirror BookingProtocol's checks exactly.
    // ------------------------------------------------------------------

    private void AddBookings()
    {
        // Roster bookings, the way a roster sync makes them: every class in start order, every roster member
        // from the day they were added, each booking checked like BookingProtocol. A refusal is a gap.
        foreach (var cls in _classes.OrderBy(c => c.StartsAt))
        {
            foreach (var (member, addedAt) in _groupById[cls.ClassGroupId].Roster)
            {
                if (addedAt >= cls.StartsAt)
                {
                    continue;
                }

                // A blocked member keeps no future booking, as BlockMember's cascade leaves it - none from
                // today on, so the cut does not depend on the hour the seed runs.
                if (member.Status == MembershipStatus.Blocked && cls.StartsAt >= _startOfToday)
                {
                    continue;
                }

                TryBook(cls, member, addedAt);
            }
        }

        // One-off entries and vouchers spent on free spots in the groups of six, inside their validity.
        var groupClasses = _classes
            .Where(c => _groupById[c.ClassGroupId].Group.IsActive && c.Capacity == 6)
            .ToList();

        foreach (var (members, count) in new[] { (_oneOffHolders, 1), (_voucherHolders, 3) })
        {
            foreach (var member in members)
            {
                var pass = _passesByMember[member.Id].Single();
                var booked = 0;

                foreach (var cls in Shuffle(groupClasses.Where(c => Covers(pass, c)).ToList()))
                {
                    if (booked == count)
                    {
                        break;
                    }

                    if (TryBook(cls, member, pass.IssuedAt) is not null)
                    {
                        booked++;
                    }
                }
            }
        }
    }

    /// <summary>
    /// One booking, if BookingProtocol would make it: a free spot, a karnet covering the class's club-local
    /// date (earliest ValidFrom first, as FindCoveringAsync orders) and an entry left on it.
    /// </summary>
    private Booking? TryBook(Class cls, Member member, DateTimeOffset notBefore)
    {
        if (_booked.Contains((cls.Id, member.Id)) || Booked(cls) >= cls.Capacity)
        {
            return null;
        }

        var pass = CoveringPass(member, ClubDate(cls));
        if (pass is null || UsedEntries(pass) >= pass.EntryCount)
        {
            return null;
        }

        var booking = AddBooking(cls, member, pass, Latest(notBefore, pass.IssuedAt, cls.CreatedAt).AddMinutes(5));
        _activeByPass[pass.Id] = UsedEntries(pass) + 1;
        return booking;
    }

    private Booking AddBooking(Class cls, Member member, MembershipPass pass, DateTimeOffset createdAt)
    {
        if (createdAt >= cls.StartsAt)
        {
            throw new InvalidOperationException("Test data: a booking would be made after its class started.");
        }

        var booking = new Booking
        {
            Id = NewId(),
            ClassId = cls.Id,
            MemberId = member.Id,
            MembershipPassId = pass.Id,
            Status = BookingStatus.Active,
            CreatedAt = createdAt,
        };

        _bookings.Add(booking);
        _booked.Add((cls.Id, member.Id));
        _activeByClass[cls.Id] = Booked(cls) + 1;
        return booking;
    }

    /// <summary>
    /// Two classes of a group of six cancelled by the club, one past and one ahead. Their bookings stay
    /// ACTIVE: CancelClass touches only the class's status, because cancelling the bookings would record
    /// that the MEMBERS cancelled. They no longer spend an entry (EntryConsumption).
    /// </summary>
    private void CancelTwoClasses()
    {
        bool Candidate(Class c, int from, int to)
        {
            var offset = ClubDate(c).DayNumber - _today.DayNumber;
            return c.Capacity == 6 && _groupById[c.ClassGroupId].Group.IsActive && Booked(c) > 0
                && offset >= from && offset <= to;
        }

        Pick(_classes.Where(c => Candidate(c, -14, -4)).ToList()).Status = ClassStatus.Cancelled;
        Pick(_classes.Where(c => Candidate(c, 3, 10)).ToList()).Status = ClassStatus.Cancelled;
    }

    // ------------------------------------------------------------------
    // Attendance and makeups - RecordAttendance, BookMakeup and MakeupRules, mirrored.
    // ------------------------------------------------------------------

    private void AddAttendanceAndMakeups()
    {
        var classById = _classes.ToDictionary(c => c.Id);

        // Absences that may become makeup items: an ordinary booking on a class that took place, of a member
        // the makeup screen would act for (not blocked).
        List<Booking> Absences(int from, int to) => _bookings
            .Where(b => b.MakeupForBookingId is null && b.Attendance is null)
            .Where(b => classById[b.ClassId].Status == ClassStatus.Scheduled)
            .Where(b => _memberById[b.MemberId].Status == MembershipStatus.Active)
            .Where(b => ClubDate(classById[b.ClassId]).DayNumber - _today.DayNumber is var d && d >= from && d <= to)
            .ToList();

        // Every MakeupState, built rather than drawn - in this order, so each takes its own absence.
        // 1. Not made up: the deadline passed with nothing booked.
        Mark(Pick(Absences(-PastDays, -MakeupRules.DeadlineDays - 1)), BookingAttendance.Makeup);

        // 2. Not made up: staff closed it by hand.
        var closed = Pick(Absences(-20, -8));
        Mark(closed, BookingAttendance.Makeup);
        closed.MakeupClosedAt = closed.AttendanceRecordedAt!.Value.AddDays(2);
        closed.MakeupClosedBy = _admins[0].UserId;

        // 3. Open: nothing booked yet, deadline ahead.
        Mark(Pick(Absences(-10, -4)), BookingAttendance.Makeup);

        // 4. Planned: a makeup booked on a class still to come.
        BookMakeupFor(Absences(-15, -4), future: true, classById);

        // 5. Made up, and 6. not made up because the makeup class was missed too.
        Mark(BookMakeupFor(Absences(-30, -12), future: false, classById), BookingAttendance.Present);
        Mark(BookMakeupFor(Absences(-30, -12), future: false, classById), BookingAttendance.Forfeited);

        // Everything else that took place: fully marked from three days back, about half of the last two days
        // still waiting, today never. Mostly "Był", some "odrobi", a few "przepada" - as the spreadsheet reads.
        foreach (var booking in _bookings.Where(b => b.Attendance is null && b.MakeupForBookingId is null))
        {
            var cls = classById[booking.ClassId];
            var offset = ClubDate(cls).DayNumber - _today.DayNumber;

            if (cls.Status == ClassStatus.Cancelled || offset >= 0 || (offset >= -2 && _random.Next(2) == 0))
            {
                continue;
            }

            var roll = _random.Next(100);
            Mark(booking, roll < 85 ? BookingAttendance.Present : roll < 95 ? BookingAttendance.Makeup : BookingAttendance.Forfeited);
        }

        void Mark(Booking booking, BookingAttendance attendance)
        {
            var cls = classById[booking.ClassId];
            var recordedAt = cls.StartsAt.AddMinutes(cls.DurationMinutes + _random.Next(10, 181));

            booking.Attendance = attendance;
            booking.AttendanceRecordedAt = recordedAt < _startOfToday ? recordedAt : _startOfToday;
            booking.AttendanceRecordedBy = _memberById[cls.InstructorMemberId].UserId;
        }
    }

    /// <summary>
    /// Marks one of <paramref name="absences"/> "odrobi" and books its one free makeup, as BookMakeup would:
    /// on another class taking place within the deadline (ahead of today, or between the absence and three
    /// days ago), with a free spot, that the member is not already on, under a karnet covering the makeup
    /// class's own date. The makeup spends no entry. Returns the makeup booking.
    /// </summary>
    private Booking BookMakeupFor(List<Booking> absences, bool future, Dictionary<Guid, Class> classById)
    {
        foreach (var absence in Shuffle(absences))
        {
            var member = _memberById[absence.MemberId];
            var absenceDate = ClubDate(classById[absence.ClassId]);
            var deadline = MakeupRules.DeadlineFor(absenceDate);

            var candidates = _classes.Where(c =>
            {
                var date = ClubDate(c);
                var inWindow = future
                    ? date > _today && date <= deadline
                    : date > absenceDate && date <= deadline && date <= _today.AddDays(-3);

                return inWindow
                    && c.Status == ClassStatus.Scheduled
                    && _groupById[c.ClassGroupId].Group.IsActive
                    && Booked(c) < c.Capacity
                    && !_booked.Contains((c.Id, member.Id))
                    && CoveringPass(member, date) is not null;
            }).ToList();

            if (candidates.Count == 0)
            {
                continue;
            }

            var makeupClass = Pick(candidates);
            var absenceClass = classById[absence.ClassId];
            var recordedAt = absenceClass.StartsAt.AddMinutes(absenceClass.DurationMinutes + 30);

            absence.Attendance = BookingAttendance.Makeup;
            absence.AttendanceRecordedAt = recordedAt;
            absence.AttendanceRecordedBy = _memberById[absenceClass.InstructorMemberId].UserId;

            var makeup = AddBooking(
                makeupClass, member, CoveringPass(member, ClubDate(makeupClass))!, recordedAt.AddHours(1));
            makeup.MakeupForBookingId = absence.Id;
            return makeup;
        }

        throw new InvalidOperationException("Test data: no absence could be given a makeup.");
    }

    private MembershipPass? CoveringPass(Member member, DateOnly date) =>
        _passesByMember.TryGetValue(member.Id, out var passes)
            ? passes.Where(p => p.ValidFrom <= date && date <= p.ValidTo).OrderBy(p => p.ValidFrom).FirstOrDefault()
            : null;

    private int Booked(Class cls) => _activeByClass.GetValueOrDefault(cls.Id);

    private int UsedEntries(MembershipPass pass) => _activeByPass.GetValueOrDefault(pass.Id);

    private static DateOnly ClubDate(Class cls) => DateOnly.FromDateTime(ClubTime.ToClubLocal(cls.StartsAt).DateTime);

    private static bool Covers(MembershipPass pass, Class cls) =>
        pass.ValidFrom <= ClubDate(cls) && ClubDate(cls) <= pass.ValidTo;

    private static DateTimeOffset Latest(params DateTimeOffset[] instants) => instants.Max();

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

    /// <summary>Everyone who may stand in front of a class: the trainers, plus admin2, who holds Trainer too.</summary>
    private List<Member> Instructors => [.. _trainers, _admins[1]];

    /// <summary>Everyone but the staff - the people the club serves.</summary>
    private IEnumerable<Member> ClubMembers() =>
        _members.Where(m => !_admins.Contains(m) && !_trainers.Contains(m));

    /// <summary>
    /// A club-local wall-clock time <paramref name="dayOffset"/> days from today, as a UTC instant.
    /// Goes through ClubTime.AddLocalDays so an 18:30 class stays 18:30 across the DST changes the window
    /// can straddle. Today's anchor itself is safe: seeded times are 06:15-21:00, nowhere near the
    /// 02:00-03:00 transition hour (midnight anchors are only compared, never displayed).
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
