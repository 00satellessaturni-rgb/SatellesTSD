using SatellesTSD.Core.Matching;

namespace SatellesTSD.Tests;

/// <summary>The workbook's name search: tokens, the exact names, the aliases, the mask walk, the uniqueness check.</summary>
public class NameSearchTests
{
    private static readonly NameSearch Search = new(MatchCatalog.Default);

    private static SearchPerson Person(string last, string first, string? code = "48712219406") => SearchPerson.Of(code, last, first);

    private static SheetSearch Find(SearchPerson person, params string?[] names) => Search.Search(person, names, []);

    [Fact]
    public void Tokens_drop_the_workbooks_symbols_without_a_space_and_split_on_single_spaces()
    {
        Assert.Equal(["Mari", "Maasikas"], NameSearch.Tokens("Mari Maasikas"));
        Assert.Equal(["MaasikasMari"], NameSearch.Tokens("Maasikas,Mari")); // the workbook deleted the comma
        Assert.Equal(["Mari", "Maasikas", "OÜ"], NameSearch.Tokens("Mari  Maasikas (OÜ)."));
        Assert.Empty(NameSearch.Tokens(null));
        Assert.Empty(NameSearch.Tokens("   "));
    }

    [Fact]
    public void Masks_walk_one_letter_at_a_time_from_the_first()
    {
        Assert.Equal(["*AMM", "T*MM", "TA*M", "TAM*"], NameSearch.Masks("TAMM"));
        Assert.Equal(["*b", "a*"], NameSearch.Masks("ab"));
        Assert.Empty(NameSearch.Masks("a"));
        Assert.Empty(NameSearch.Masks(""));
    }

    [Fact]
    public void The_exact_names_hit_as_tokens_in_any_order_and_any_case_and_every_such_row()
    {
        var found = Find(Person("MAASIKAS", "MARI"), "Mari Maasikas", "Jaan Tamm", "MAASIKAS MARI", "Maasikas Mari-Liis", "Mari Maasikas OÜ");

        Assert.Equal([0, 2, 4], found.Hits); // "Mari-Liis" is one token, not "Mari"
        Assert.False(found.MaskUsed);
        Assert.Equal("", found.FullMask);
        Assert.Equal(("MAASIKAS", "MARI"), (found.LastMask, found.FirstMask));
    }

    [Fact]
    public void A_symbol_glued_to_a_name_keeps_it_from_matching_as_the_workbook_had_it()
    {
        Assert.Empty(Find(Person("MAASIKAS", "MARI"), "Maasikas,Mari").Hits);
        Assert.Equal([0], Find(Person("MAASIKAS", "MARI"), "Maasikas, Mari").Hits);
    }

    [Fact]
    public void An_alias_of_the_first_name_hits_after_the_exact_name_fails_and_shows_as_the_mask()
    {
        var found = Find(Person("KIIASHKO", "SERHII"), "Sergei Kiiashko", "Serhii Kiiashko");

        Assert.Equal([1], found.Hits); // the exact first name is there: the alias is never tried
        var aliasOnly = Find(Person("KIIASHKO", "SERHII"), "Sergei Kiiashko");
        Assert.Equal([0], aliasOnly.Hits);
        Assert.True(aliasOnly.MaskUsed);
        Assert.Equal("KIIASHKO SERGEI", aliasOnly.FullMask);
        Assert.Empty(Find(Person("KIIASHKO", "JAAN"), "Sergei Kiiashko").Hits);
    }

    [Fact]
    public void The_last_name_is_masked_when_no_row_carries_it_and_the_first_name_when_a_row_does()
    {
        var lastMasked = Find(Person("JANĪTIS", "SANDIS"), "sandis janitis");
        Assert.Equal([0], lastMasked.Hits);
        Assert.Equal(("JAN*TIS SANDIS", true), (lastMasked.FullMask, lastMasked.MaskUsed));

        var firstMasked = Find(Person("CHISNICEAN", "VICTOR"), "Viktor Chisnicean");
        Assert.Equal([0], firstMasked.Hits);
        Assert.Equal("CHISNICEAN VI*TOR", firstMasked.FullMask);

        var both = Find(Person("GERASSIMTŠUK", "VJATŠESLAV"), "Vjatseslav Gerassimtsuk");
        Assert.Equal("GERASSIMT*UK VJAT*ESLAV", both.FullMask);

        var alias = Find(Person("KIIASHKO", "SERHII"), "Sergei Kiiasko");
        Assert.Equal("KIIAS*KO SERGEI", alias.FullMask); // the workbook's own Otsing* text

        var letterLost = Find(Person("LUTŠIN", "ALEKSEI"), "Aleksei Lutshin");
        Assert.Equal("LUT*IN ALEKSEI", letterLost.FullMask); // one star stands for "sh"
    }

    [Fact]
    public void A_question_mark_written_by_the_bank_is_reached_by_the_mask_that_covers_that_letter()
    {
        var found = Find(Person("ŞINCARIOV", "EVGHENI"), "Evgheni ?incariov");
        Assert.Equal([0], found.Hits);
        Assert.Equal("*INCARIOV EVGHENI", found.FullMask);

        var two = Find(Person("PÕLD", "JÜRI"), "J?ri P?ld");
        Assert.Equal("P*LD J*RI", two.FullMask);
    }

    [Fact]
    public void Once_a_row_carries_the_last_name_no_deeper_last_mask_is_tried()
    {
        // row 0 carries TAMM but not the first name in any form; row 1 would need the last mask "TAM*": the workbook moved on
        Assert.Empty(Find(Person("TAMM", "JAAN"), "Tamm Peeter", "Tamme Jaan").Hits);
        Assert.Equal([0], Find(Person("TAMM", "JAAN"), "Tamme Jaan").Hits); // without the exact-last row the mask walk reaches it
    }

    [Fact]
    public void A_masked_hit_that_could_also_mean_another_person_of_the_register_is_dropped()
    {
        var jaan = Person("TAMM", "JAAN", "1");
        var jaanToo = Person("TAMME", "JAAN", "2");
        var found = Search.Search(jaan, ["Jaan Tamme"], [jaan, jaanToo]);

        Assert.Empty(found.Hits);
        Assert.True(found.Ambiguous);
        Assert.Equal(jaanToo, found.ConflictsWith);
        Assert.Equal([0], found.Dropped);
        Assert.Equal("TAM* JAAN", found.FullMask);

        // an exact hit is never checked, even when another person's name contains it
        var exact = Search.Search(jaan, ["Jaan Tamm"], [jaan, jaanToo]);
        Assert.Equal([0], exact.Hits);

        // the check is on the masks, so a stranger whose last name merely contains the mask and whose first name fits is enough
        var mari = Person("TAMMSAAR", "JAAN", "3");
        Assert.True(Search.Search(jaan, ["Jaan Tamme"], [mari]).Ambiguous);
        Assert.False(Search.Search(jaan, ["Jaan Tamme"], [Person("TAMMSAAR", "MARI", "4")]).Ambiguous);
        Assert.False(Search.Search(jaan, ["Jaan Tamme"], [jaan with { Key = jaan.Key }]).Ambiguous); // the same person is no competitor
    }

    [Fact]
    public void At_the_alias_level_each_row_carries_its_own_alias_and_is_checked_for_it()
    {
        // the register has two persons whose first names are both in one alias group; the bank spells each its own way
        var serhii = Person("TAMM", "SERHII", "1");
        var sergii = Person("TAMM", "SERGII", "2");
        var found = Search.Search(serhii, ["Sergei Tamm", "Sergii Tamm"], [serhii, sergii]);

        Assert.Equal([0], found.Hits); // "Sergii" is the other person's exact first name: that row is theirs
        Assert.Equal(["SERGEI"], found.HitMasks);
        Assert.Equal(("TAMM SERGEI", true), (found.FullMask, found.Ambiguous));
        Assert.Equal([1], found.Dropped);
        Assert.Equal(sergii, found.ConflictsWith);
        Assert.Equal([1], Search.Search(sergii, ["Sergei Tamm", "Sergii Tamm"], [serhii, sergii]).Hits);

        // alone in the register, both spellings are the person's, each row with the alias that found it
        var alone = Search.Search(serhii, ["Sergei Tamm", "Sergii Tamm"], [serhii]);
        Assert.Equal([0, 1], alone.Hits);
        Assert.Equal(["SERGEI", "SERGII"], alone.HitMasks);
        Assert.Equal("TAMM SERGEI", alone.FullMask);
        Assert.False(alone.Ambiguous);

        // the exact spelling present: the exact level takes it and the alias level is never reached
        Assert.Equal([2], Search.Search(serhii, ["Sergei Tamm", "Sergii Tamm", "Serhii Tamm"], [serhii]).Hits);
    }

    [Fact]
    public void Two_persons_without_a_personal_code_never_compete_as_the_workbook_compared_the_codes()
    {
        var jaan = Person("TAMM", "JAAN", null);
        var other = Person("TAMMIK", "JAAN", null);
        var found = Search.Search(jaan, ["Jaan Tam"], [jaan, other]);

        Assert.Equal([0], found.Hits); // "TAM*" would also mean TAMMIK JAAN, but two codeless rows were one code ("") to the workbook
        Assert.False(found.Ambiguous);
        Assert.True(Search.Search(jaan, ["Jaan Tam"], [jaan, Person("TAMMIK", "JAAN", "9")]).Ambiguous);
        Assert.True(Search.Search(Person("TAMM", "JAAN", "1"), ["Jaan Tam"], [other]).Ambiguous); // a coded person and a codeless one do compete
    }

    [Fact]
    public void Names_the_workbook_could_not_match_are_still_not_matched()
    {
        Assert.Empty(Find(Person("BEN ALI", "MARI"), "Mari Ben Ali").Hits); // two words never equal one token
        Assert.Empty(Find(Person("MAASIKAS", "ANNA MARIA"), "Anna Maria Maasikas").Hits);
        Assert.Empty(Find(Person("", "MARI"), "Mari Maasikas").Hits);
        Assert.Empty(Find(Person("MAASIKAS", ""), "Mari Maasikas").Hits);
        Assert.Empty(Find(Person("MAASIKAS", "MARI"), null, "", "   ").Hits);
    }

    [Fact]
    public void A_person_is_keyed_by_the_code_else_by_the_names()
    {
        Assert.Equal("48712219406", Person("MAASIKAS", "MARI").Key);
        Assert.Equal("MAASIKAS|MARI", SearchPerson.Of(null, " Maasikas ", "Mari").Key);
        Assert.Equal(("Maasikas", "Mari"), (SearchPerson.Of(" ", " Maasikas ", "Mari ").LastName, SearchPerson.Of(" ", " Maasikas ", "Mari ").FirstName));
        Assert.Equal("Maasikas Mari", SearchPerson.Of(null, "Maasikas", "Mari").FullName);
    }
}
