namespace po_prostu_silka.Application.Scheduling;

/// <summary>
/// One past class of the member's, as their history shows it (S-27, AT-04).
/// </summary>
/// <param name="Outcome">
/// <c>present</c>, <c>absent</c>, <c>unrecorded</c> or <c>cancelled</c>. <c>cancelled</c> wins over any
/// mark: nobody attends a cancelled class, and the member is owed the reason their entry came back.
/// </param>
public record MyAttendanceEntry(
    Guid BookingId,
    Guid ClassId,
    string Name,
    DateTimeOffset StartsAt,
    int DurationMinutes,
    string Instructor,
    string Outcome);

/// <summary>
/// The member's current karnet, read as attendance rather than as a balance: of the classes it has
/// paid for that already happened, how many they came to, missed, or nobody recorded.
/// </summary>
/// <param name="EntryCount">The pass's issued entries, so the view can draw one mark per entry.</param>
public record MyAttendanceSummary(
    string TypeName,
    DateOnly ValidFrom,
    DateOnly ValidTo,
    int EntryCount,
    int Present,
    int Absent,
    int Unrecorded);

/// <summary>
/// One page of the member's history: three club-local calendar months, newest first.
/// </summary>
/// <param name="Summary">Only on the first page, and only while a karnet covers today.</param>
/// <param name="EarlierBefore">
/// The <c>before</c> value for the next page back, or null when the member has nothing older.
/// </param>
public record MyAttendanceHistory(
    MyAttendanceSummary? Summary,
    IReadOnlyList<MyAttendanceEntry> Items,
    DateOnly? EarlierBefore);

/// <summary>The three attendance counts of one karnet's started, non-cancelled bookings.</summary>
public record AttendanceCounts(int Present, int Absent, int Unrecorded);
