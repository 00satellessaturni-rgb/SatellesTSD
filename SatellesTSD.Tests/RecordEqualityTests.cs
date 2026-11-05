using System.Collections;
using System.Reflection;
using SatellesTSD.Core.Import;
using SatellesTSD.Core.Lookups;
using SatellesTSD.Core.Matching;
using SatellesTSD.Core.Store;

namespace SatellesTSD.Tests;

/// <summary>The import and matching records replace the compiler's equality (a record compares a list by reference).
/// This guard keeps the hand-written Equals honest: a copy that differs in any one constructor parameter must not be
/// equal, so a member added later without a line in Equals fails here rather than letting a store that forgets to
/// persist it pass every round-trip test.</summary>
public class RecordEqualityTests
{
    private static readonly StatementRow SampleRow = new(12, new DateOnly(2026, 10, 5), "05.10.2026", "EE49", "Mari Maasikas", "48712219406", -905.92m, "-905,92", "EUR", "palk", "ref", "tx1",
        Direction.Debit, "D", "NoName", "problem", "hash", ["a", "b"]);

    private static readonly TorEntry SampleEntry = new(2, "414389", "38810140309", "DMITRI", "BAVÕKIN", "EE", new DateOnly(2012, 3, 1), new DateOnly(2026, 9, 30), "1", "Tööleping", "2", "Kehtiv", "7", "Lõpetamine",
        "12223047", "STOPFIRE OÜ", ["x"], "problem");

    private static readonly SearchPerson SamplePerson = new("38810140309", "BAVÕKIN", "DMITRI", "38810140309");

    private static readonly Category SampleCategory = new("Palgad", Direction.Debit, true, true, true, false, false, "g", "c");

    private static readonly CategoryVerdict SampleVerdict = new(VerdictKind.Matched, SampleCategory, "[Palgad]", ["Palgad"]);

    private static readonly MatchSource SampleSource = new("LHV pank *1757", "lhv-et-short", LayoutKind.Bank, [SampleRow], Guid.NewGuid());

    private static readonly MatchPerson SampleMatchPerson = new(SamplePerson, [SampleEntry], SampleEntry, 3);

    private static readonly MatchHit SampleHit = new("LHV pank *1757", SampleRow, "38810140309", "BAV*KIN", "DMITRI", true, SampleVerdict, "[Palgad]", true, true, ["f"]);

    private static readonly MatchProblem SampleProblem = new("CODE", "message", "key", "source", 2);

    public static IEnumerable<object[]> Samples()
    {
        var layout = LayoutCatalog.Default.Find("swedbank-et")! with
        {
            DateFormats = ["dd-MM-yyyy"], DecimalSeparator = ",", TimeZone = "UTC", AmountHasCurrency = true, AllowHeaderless = true, HeaderlessAccountPattern = "^EE", Notes = "n",
        };
        yield return [layout];
        yield return [SampleRow];
        yield return [SampleEntry];
        yield return
        [
            new ImportSheet(Guid.NewGuid(), Guid.NewGuid(), 1, "LHV", SheetKind.Statement, "lhv-et-short", "{}", 1, ["a", "b"], "EE49", "LHV pank *1757", 3,
                new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31), [new ImportProblem(2, "BAD_DATE", "x")]),
        ];

        // matching
        yield return [new PatternList(["a"], ["b"])];
        yield return [new SheetSearch([1, 2], "BAV*KIN", "DMITRI", true, SamplePerson, [3], ["DMITRI", "DMITRI"])];
        yield return [SampleVerdict];
        yield return [SampleSource];
        yield return [SampleMatchPerson];
        yield return [SampleHit];
        yield return [new SubTableRow("k", "name", new Dictionary<string, decimal> { ["LHV pank *1757"] = 1m })];
        yield return [new MatchResult(2026, 10, [SampleMatchPerson], [SampleSource], [SampleHit], [SampleProblem], "{}")];
        yield return [new StoredMatchRun(Guid.NewGuid(), Guid.NewGuid(), new DateTime(2026, 11, 3, 12, 0, 0, DateTimeKind.Utc), "{}", [SampleProblem], Guid.NewGuid())];
        yield return [new StoredMatchPerson(Guid.NewGuid(), Guid.NewGuid(), 1, "key", "38810140309", "BAVÕKIN", "DMITRI", Guid.NewGuid(), [Guid.NewGuid()])];
        yield return
        [
            new StoredMatchHit(Guid.NewGuid(), Guid.NewGuid(), 1, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "BAV*KIN", "DMITRI", true, VerdictKind.Matched, "Palgad", ["Palgad"], "[Palgad]", true, true, ["f"]),
        ];

        // lookups
        yield return [new ResidencyFinding(new DateOnly(2026, 11, 4), [SamplePeriod], ["tõend"])];
        yield return [new PensionFinding(PensionStatus.Joined, new DateOnly(2019, 1, 1), new DateOnly(2026, 8, 1), [SampleRate])];
        yield return [SampleOutcome];
        yield return [new LookupRunReport(new DateOnly(2026, 10, 31), [SampleOutcome], ["p"])];
    }

    private static readonly DatePeriod SamplePeriod = new(new DateOnly(2024, 1, 1), new DateOnly(2025, 12, 31));

    private static readonly RatePeriod SampleRate = new(new DateOnly(2019, 1, 1), null, 0.02m, "Vaikimisi määr");

    private static readonly PersonLookupOutcome SampleOutcome = new(Guid.NewGuid(), "48712219406", "Mari Maasikas", ["asked residency"], ["p"]);

    [Theory]
    [MemberData(nameof(Samples))]
    public void A_copy_that_differs_in_any_one_member_is_not_equal_and_a_true_copy_is(object sample)
    {
        var type = sample.GetType();
        var constructor = type.GetConstructors().OrderByDescending(c => c.GetParameters().Length).First();
        var parameters = constructor.GetParameters();
        var values = parameters.Select(p => type.GetProperty(p.Name!, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase)!.GetValue(sample)).ToArray();

        Assert.Equal(sample, constructor.Invoke(values));
        Assert.Equal(sample.GetHashCode(), constructor.Invoke(values).GetHashCode());

        for (var i = 0; i < parameters.Length; i++)
        {
            var mutated = (object?[])values.Clone();
            mutated[i] = Mutate(parameters[i].ParameterType, values[i]);
            Assert.False(sample.Equals(constructor.Invoke(mutated)), $"{type.Name}: a copy with another {parameters[i].Name} still compares equal.");
        }
    }

    private static object? Mutate(Type type, object? value)
    {
        var underlying = Nullable.GetUnderlyingType(type) ?? type;
        if (underlying == typeof(string))
        {
            return (value as string ?? "") + "x";
        }

        if (underlying == typeof(int))
        {
            return (value is int i ? i : 0) + 1;
        }

        if (underlying == typeof(bool))
        {
            return !(value is true);
        }

        if (underlying == typeof(decimal))
        {
            return (value is decimal d ? d : 0m) + 1m;
        }

        if (underlying == typeof(DateOnly))
        {
            return (value is DateOnly date ? date : new DateOnly(2026, 1, 1)).AddDays(1);
        }

        if (underlying == typeof(DateTime))
        {
            return (value is DateTime time ? time : new DateTime(2026, 1, 1)).AddDays(1);
        }

        if (underlying == typeof(Guid))
        {
            return Guid.NewGuid();
        }

        if (underlying.IsEnum)
        {
            return Enum.GetValues(underlying).Cast<object>().First(v => !v.Equals(value));
        }

        if (underlying == typeof(LayoutRoles))
        {
            return (value as LayoutRoles ?? new LayoutRoles(null, "d", "c", "a", null, null)) with { Account = "changed" };
        }

        if (underlying == typeof(SearchPerson))
        {
            return (value as SearchPerson ?? SamplePerson) with { Key = "other" };
        }

        if (underlying == typeof(TorEntry))
        {
            return (value as TorEntry ?? SampleEntry) with { RowNo = 99 };
        }

        if (underlying == typeof(StatementRow))
        {
            return (value as StatementRow ?? SampleRow) with { RowNo = 99 };
        }

        if (underlying == typeof(Category))
        {
            return (value as Category ?? SampleCategory) with { Name = "other" };
        }

        if (underlying == typeof(CategoryVerdict))
        {
            return (value as CategoryVerdict ?? SampleVerdict) with { Text = "other" };
        }

        if (typeof(IEnumerable<string>).IsAssignableFrom(underlying))
        {
            return ((value as IEnumerable<string>)?.ToList() ?? []).Append("x").ToList();
        }

        if (typeof(IEnumerable<int>).IsAssignableFrom(underlying))
        {
            return ((value as IEnumerable<int>)?.ToList() ?? []).Append(99).ToList();
        }

        if (typeof(IEnumerable<Guid>).IsAssignableFrom(underlying))
        {
            return ((value as IEnumerable<Guid>)?.ToList() ?? []).Append(Guid.NewGuid()).ToList();
        }

        if (typeof(IReadOnlyDictionary<string, decimal>).IsAssignableFrom(underlying))
        {
            var copy = new Dictionary<string, decimal>(value as IReadOnlyDictionary<string, decimal> ?? new Dictionary<string, decimal>()) { ["other"] = 2m };
            return copy;
        }

        foreach (var (elementType, fresh) in new (Type, object)[]
                 {
                     (typeof(ImportProblem), new ImportProblem(null, "X", "x")), (typeof(TorEntry), SampleEntry with { RowNo = 99 }), (typeof(StatementRow), SampleRow with { RowNo = 99 }),
                     (typeof(MatchProblem), SampleProblem with { Code = "X" }), (typeof(MatchPerson), SampleMatchPerson with { Order = 99 }), (typeof(MatchSource), SampleSource with { Name = "x" }),
                     (typeof(MatchHit), SampleHit with { Text = "x" }),
                     (typeof(DatePeriod), SamplePeriod with { End = null }), (typeof(RatePeriod), SampleRate with { Rate = 0.04m }), (typeof(PersonLookupOutcome), SampleOutcome with { Name = "x" }),
                 })
        {
            if (typeof(IEnumerable<>).MakeGenericType(elementType).IsAssignableFrom(underlying))
            {
                var list = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(elementType))!;
                foreach (var item in (value as IEnumerable) ?? Array.Empty<object>())
                {
                    list.Add(item);
                }

                list.Add(fresh);
                return list;
            }
        }

        throw new NotSupportedException($"No mutation for {type}; extend the guard.");
    }
}
