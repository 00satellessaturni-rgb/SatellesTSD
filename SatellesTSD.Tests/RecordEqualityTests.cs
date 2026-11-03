using System.Collections;
using System.Reflection;
using SatellesTSD.Core.Import;
using SatellesTSD.Core.Store;

namespace SatellesTSD.Tests;

/// <summary>The import records replace the compiler's equality (a record compares a list by reference). This guard
/// keeps the hand-written Equals honest: a copy that differs in any one constructor parameter must not be equal, so
/// a member added later without a line in Equals fails here rather than letting a store that forgets to persist it
/// pass every round-trip test.</summary>
public class RecordEqualityTests
{
    public static IEnumerable<object[]> Samples()
    {
        var layout = LayoutCatalog.Default.Find("swedbank-et")! with
        {
            DateFormats = ["dd-MM-yyyy"], DecimalSeparator = ",", TimeZone = "UTC", AmountHasCurrency = true, AllowHeaderless = true, HeaderlessAccountPattern = "^EE", Notes = "n",
        };
        yield return [layout];

        yield return
        [
            new StatementRow(12, new DateOnly(2026, 10, 5), "05.10.2026", "EE49", "Mari Maasikas", "48712219406", -905.92m, "-905,92", "EUR", "palk", "ref", "tx1",
                Direction.Debit, "D", "NoName", "problem", "hash", ["a", "b"]),
        ];

        yield return
        [
            new TorEntry(2, "414389", "38810140309", "DMITRI", "BAVÕKIN", "EE", new DateOnly(2012, 3, 1), new DateOnly(2026, 9, 30), "1", "Tööleping", "2", "Kehtiv", "7", "Lõpetamine",
                "12223047", "STOPFIRE OÜ", ["x"], "problem"),
        ];

        yield return
        [
            new ImportSheet(Guid.NewGuid(), Guid.NewGuid(), 1, "LHV", SheetKind.Statement, "lhv-et-short", "{}", 1, ["a", "b"], "EE49", "LHV pank *1757", 3,
                new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31), [new ImportProblem(2, "BAD_DATE", "x")]),
        ];
    }

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

        if (typeof(IEnumerable<string>).IsAssignableFrom(underlying))
        {
            return ((value as IEnumerable<string>)?.ToList() ?? []).Append("x").ToList();
        }

        if (typeof(IEnumerable<ImportProblem>).IsAssignableFrom(underlying))
        {
            return ((value as IEnumerable<ImportProblem>)?.ToList() ?? []).Append(new ImportProblem(null, "X", "x")).ToList();
        }

        throw new NotSupportedException($"No mutation for {type}; extend the guard.");
    }
}
