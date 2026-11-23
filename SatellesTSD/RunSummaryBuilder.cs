using SatellesTSD.Core.Lookups;
using SatellesTSD.Core.Matching;
using SatellesTSD.Core.Review;

namespace SatellesTSD;

/// <summary>The summary of a run, made from what the grids show once it is over — the review as it was reloaded, and the
/// register check's report (or, when the check was stopped, the persons it got to) — so the counts are the grids' own.
/// Each kind of problem counts a place once: one occurrence per code and person and payment (or source row), however many
/// lists the review says it in (a person's problems and the run's own list overlap; a refused payment is in the check cell
/// and among the person's undeclared income).</summary>
internal static class RunSummaryBuilder
{
    /// <param name="outcomes">The persons the check got to, in its order: the report's when it ended, else those reported so far.</param>
    public static RunSummary Build(ReviewView view, DateTime madeAt, bool searched, bool checkedRegisters, bool completed, LookupRunReport? report, IReadOnlyList<PersonLookupOutcome>? outcomes)
    {
        ArgumentNullException.ThrowIfNull(view);
        var sections = new List<SummarySection>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        if (searched)
        {
            sections.Add(Section(RunSummary.SearchSection, UiText.SummarySearch, SearchProblems(view, seen)));
        }

        // the payments are checked by the facts the registers set: after a check alone they are counted afresh too, so the
        // summary's counts are the check cell's
        sections.Add(Section(RunSummary.PaymentsSection, UiText.SummaryPayments, PaymentProblems(view, seen)));
        if (checkedRegisters)
        {
            sections.Add(Section(RunSummary.RegistersSection, UiText.SummaryRegisters, RegisterProblems(view, report, report?.Persons ?? outcomes ?? [], seen)));
        }

        return new RunSummary(view.Company.Id, view.Company.Name, view.Company.RegistryCode, view.Period.Id, view.Period.Year, view.Period.Month, madeAt,
            searched, checkedRegisters, !checkedRegisters || completed, sections);
    }

    private static SummarySection Section(string kind, string title, List<(string Code, SummaryOccurrence Occurrence)> found) =>
        new(kind, title, found.GroupBy(f => f.Code, StringComparer.Ordinal)
            .Select(g => new SummaryGroup(g.Key, UiText.ShortTitle(g.Key), g.Select(f => f.Occurrence).ToList()))
            .OrderByDescending(g => g.Count).ThenBy(g => g.Code, StringComparer.Ordinal).ToList()); // the most frequent first

    /// <summary>The person problems of the Probleemid column — the run's and the review's own, the rows nobody matched
    /// included — the run's problems of no person, and the income the column says goes undeclared not by choice.</summary>
    private static List<(string, SummaryOccurrence)> SearchProblems(ReviewView view, HashSet<string> seen)
    {
        var found = new List<(string, SummaryOccurrence)>();
        void Problem(MatchProblem problem, ReviewPerson? person)
        {
            var personId = person is { IsUnmatched: false } ? person.PersonId : null;
            // the same problem said in two lists is one: the run's problem of a person is in the person's own list too
            if (seen.Add($"P|{problem.Code}|{personId}|{problem.Source}|{problem.RowNo}|{problem.Subject}|{problem.Message}"))
            {
                var who = person is { IsUnmatched: false } ? Name(person) : SourceOf(problem);
                found.Add((problem.Code, new SummaryOccurrence(who, UiText.ProblemLine(problem), personId)));
            }
        }

        foreach (var person in view.Persons)
        {
            foreach (var problem in person.Problems)
            {
                Problem(problem, person);
            }
        }

        foreach (var problem in view.Problems)
        {
            var person = problem.PersonKey is { } key ? view.Persons.FirstOrDefault(p => p.Key == key) : null;
            if (person is null && problem.PersonKey is { } other && view.Run?.Persons.Any(p => p.Key == other) == true)
            {
                // a second register row bound to a person shown once: the Probleemid column shows the first row's problems
                // alone, and the summary counts what the grids show — so not here either
                continue;
            }

            Problem(problem, person);
        }

        foreach (var person in view.Persons)
        {
            foreach (var undeclared in person.Undeclared.Where(u => !u.ByChoice && u.Reason != Undeclared.Refused))
            {
                // a refused payment is the check cell's, counted with the payments; held or not declared by default is the column's
                var payment = undeclared.Payment;
                foreach (var code in undeclared.Codes.Count > 0 ? undeclared.Codes : [undeclared.Reason])
                {
                    var blocker = payment.Blockers.FirstOrDefault(b => b.Code == code);
                    var detail = blocker is not null ? UiText.RefusalLine(blocker) : $"{code}: {UiText.UndeclaredReason(undeclared)}";
                    Payment(found, seen, "U", code, person, payment, detail);
                }
            }
        }

        return found;
    }

    /// <summary>The check cell's refusals of every payment (each code once a payment), and its notices.</summary>
    private static List<(string, SummaryOccurrence)> PaymentProblems(ReviewView view, HashSet<string> seen)
    {
        var found = new List<(string, SummaryOccurrence)>();
        foreach (var person in view.Persons)
        {
            foreach (var payment in person.Payments)
            {
                foreach (var refusal in payment.Checks)
                {
                    Payment(found, seen, "C", refusal.Code, person, payment, UiText.RefusalLine(refusal));
                }

                foreach (var notice in payment.Notices)
                {
                    Payment(found, seen, "C", notice, person, payment, UiText.NoticeTip(payment, notice));
                }
            }
        }

        return found;
    }

    private static void Payment(List<(string, SummaryOccurrence)> found, HashSet<string> seen, string list, string code, ReviewPerson person, ReviewPayment payment, string detail)
    {
        var personId = person.IsUnmatched ? null : person.PersonId;
        if (!seen.Add($"{list}|{code}|{personId}|{payment.RowId}|{payment.LineId}|{payment.Part}"))
        {
            return;
        }

        var who = person.IsUnmatched ? payment.IsByHand ? payment.Payee : payment.Reference : $"{Name(person)} — {(payment.IsByHand ? UiText.ByHand : payment.Reference)}";
        found.Add((code, new SummaryOccurrence(who, detail, personId, payment.Date, payment.AmountText is null ? payment.Amount : null, payment.RowId, payment.LineId, payment.IsPart)));
    }

    /// <summary>The register check's problems of each person it got to, and its own (no search, a person without a code).</summary>
    /// <param name="report">The check's report — or, for a check stopped or failed, the run's own problems it told before
    /// the first query, with the persons it got to.</param>
    private static List<(string, SummaryOccurrence)> RegisterProblems(ReviewView view, LookupRunReport? report, IReadOnlyList<PersonLookupOutcome> outcomes, HashSet<string> seen)
    {
        var found = new List<(string, SummaryOccurrence)>();
        foreach (var outcome in outcomes)
        {
            // named as the persons grid names them, as in the other sections: the check has the store's „Eesnimi Perekonnanimi“
            var shown = outcome.PersonId == Guid.Empty ? null : view.Persons.FirstOrDefault(p => !p.IsUnmatched && p.PersonId == outcome.PersonId);
            for (var i = 0; i < outcome.Problems.Count; i++)
            {
                var note = outcome.ProblemNotes.Count == outcome.Problems.Count ? outcome.ProblemNotes[i] : null;
                var detail = UiText.LookupProblemLine(outcome.Problems[i], note);
                var code = note?.Code ?? CodeOf(outcome.Problems[i]);
                if (seen.Add($"R|{code}|{outcome.PersonId}|{detail}"))
                {
                    found.Add((code, new SummaryOccurrence(shown is not null ? Name(shown) : outcome.Name, detail, outcome.PersonId == Guid.Empty ? null : outcome.PersonId)));
                }
            }
        }

        if (report is not null)
        {
            for (var i = 0; i < report.Problems.Count; i++)
            {
                var note = report.ProblemNotes.Count == report.Problems.Count ? report.ProblemNotes[i] : null;
                var detail = UiText.LookupProblemLine(report.Problems[i], note);
                var code = note?.Code ?? CodeOf(report.Problems[i]);
                if (seen.Add($"R|{code}||{detail}"))
                {
                    // a person without a code is one of the persons grid's (a codeless person paid in the month by their id, a
                    // register row by its key, as the review makes them): gone to by a double-click, named as the grid names them
                    var person = note?.PersonId is { } id ? view.Persons.FirstOrDefault(p => !p.IsUnmatched && p.PersonId == id) : null;
                    person ??= note?.PersonKey is { } key ? view.Persons.FirstOrDefault(p => !p.IsUnmatched && p.Key == key) : null;
                    found.Add((code, person is not null
                        ? new SummaryOccurrence(Name(person), detail, person.PersonId)
                        : new SummaryOccurrence(note?.Name ?? UiText.SummaryWholeCheck, detail)));
                }
            }
        }

        return found;
    }

    /// <summary>The code an English line of the Core leads with ("FAILED: …"), for a line that came without its note; empty when it leads with none.</summary>
    private static string CodeOf(string english)
    {
        var colon = english.IndexOf(':', StringComparison.Ordinal);
        var head = colon > 0 ? english[..colon] : "";
        return head.Length > 0 && head.All(c => char.IsAsciiLetterUpper(c) || c == '_') ? head : "";
    }

    /// <summary>A person as the persons grid names them: „Perekonnanimi Eesnimi“.</summary>
    private static string Name(ReviewPerson person) => $"{person.LastName} {person.FirstName}".Trim();

    /// <summary>A problem of no person by its file and row, as the review names a row: „LHV pank *1757 [12]“.</summary>
    private static string SourceOf(MatchProblem problem) => problem.Source is { } source
        ? problem.RowNo is { } row ? $"{source} [{row}]" : source
        : UiText.SummaryNoPerson;
}
