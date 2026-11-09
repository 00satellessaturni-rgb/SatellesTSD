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
    public void A_name_of_several_words_is_found_as_consecutive_tokens_in_the_banks_spaced_spelling()
    {
        // decision 143, past the workbook: the words of a two-word surname or a double first name match that many
        // consecutive tokens, in order, at the exact level — no mask, so no uniqueness check
        var benAli = Person("BEN ALI", "MARI");
        var spaced = Find(benAli, "Mari Ben Ali");
        Assert.Equal([0], spaced.Hits);
        Assert.Equal((false, ""), (spaced.MaskUsed, spaced.FullMask));
        Assert.Equal([0], Find(benAli, "Ben Ali Mari").Hits); // the surname first, as the banks mostly write it
        Assert.Equal([0], Find(benAli, "Ben Ali, Mari").Hits); // the symbols deleted first, as before
        Assert.Equal([0], Find(Person("MAASIKAS", "ANNA MARIA"), "Anna Maria Maasikas").Hits);
        Assert.Equal([0], Find(Person("BEN ALI", "ANNA MARIA"), "Anna Maria Ben Ali").Hits);

        // the words out of order or apart are not that name: nothing at the exact level, and no mask makes them one
        Assert.Empty(Find(benAli, "Mari Ali Ben").Hits);
        Assert.Empty(Find(benAli, "Ben Mari Ali").Hits);
        Assert.Empty(Find(Person("MAASIKAS", "ANNA MARIA"), "Maria Anna Maasikas").Hits);

        // the glued and the hyphenated spellings are found as the workbook found them: through the one-word mask over
        // the space, tried before the spaced masks, with the uniqueness check — a masked level, so the spaced spelling
        // found at the exact level leaves a glued row of the same source for the hand (the first level that hits takes it)
        var glued = Find(benAli, "Mari BenAli");
        Assert.Equal([0], glued.Hits);
        Assert.Equal((true, "BEN*ALI MARI"), (glued.MaskUsed, glued.FullMask));
        var hyphened = Find(benAli, "Mari Ben-Ali");
        Assert.Equal([0], hyphened.Hits);
        Assert.Equal("BEN*ALI", hyphened.LastMask);
        Assert.Equal([0], Find(benAli, "Mari Ben Ali", "Mari BenAli").Hits);
        var behind = Find(benAli, "Kati Ben Ali", "Mari BenAli"); // another person's spaced row does not end the search before the glued one
        Assert.Equal([1], behind.Hits);
        Assert.Equal("BEN*ALI", behind.LastMask);
        var joined = Find(Person("MAASIKAS", "ANNA MARIA"), "Hanna Maria Maasikas", "Anna-Maria Maasikas"); // the joining before a masked letter
        Assert.Equal([1], joined.Hits);
        Assert.Equal("ANNA*MARIA", joined.FirstMask);
        Assert.Equal(["BEN*ALI", "*EN ALI", "B*N ALI", "BE* ALI", "BEN *LI", "BEN A*I", "BEN AL*"], NameSearch.MasksInOrder("BEN ALI"));
        Assert.Equal(NameSearch.Masks("TAMM"), NameSearch.MasksInOrder("TAMM"));

        // a one-word name the glued spelling of a spaced name is: the spaced name's hit through the mask asks the
        // competitors, as the workbook did, and the row is the one-word name's
        var mariliis = Person("TAMM", "MARILIIS", "9");
        var mariLiis = Search.Search(Person("TAMM", "MARI LIIS"), ["Mariliis Tamm"], [mariliis]);
        Assert.True(mariLiis.Ambiguous);
        Assert.Empty(mariLiis.Hits);
        Assert.Equal([0], Find(mariliis, "Mariliis Tamm").Hits);

        // a letter masked in one of the words still asks for both words, consecutive
        var masked = Find(benAli, "Mari Ben Aly");
        Assert.Equal([0], masked.Hits);
        Assert.Equal((true, "BEN AL*"), (masked.MaskUsed, masked.LastMask));

        // the uniqueness check: a competitor whose surname holds the masked words takes the rows, as before — and so does
        // one whose surname is one word of them: "Mari Sen Ali" found through "*EN ALI" could be ALI MARI's row
        var ambiguous = Search.Search(benAli, ["Mari Ben Aly"], [benAli, Person("BEN ALY", "MARI", "9")]);
        Assert.True(ambiguous.Ambiguous);
        Assert.Empty(ambiguous.Hits);
        var oneWord = Search.Search(benAli, ["Mari Sen Ali"], [benAli, Person("ALI", "MARI", "9")]);
        Assert.True(oneWord.Ambiguous);
        Assert.Empty(oneWord.Hits);
        Assert.Equal([0], Find(Person("ALI", "MARI", "9"), "Mari Sen Ali").Hits); // whose row it is
        Assert.True(NameSearch.CouldMean(Person("ALI", "MARI", "9"), "*EN ALI", "MARI"));
        Assert.True(NameSearch.CouldMean(Person("MAASIKAS", "MARIA", "9"), "MAASIKAS", "ANNA MAR*"));
        Assert.False(NameSearch.CouldMean(Person("TAMM", "MARI", "9"), "*EN ALI", "MARI"));
        Assert.False(NameSearch.CouldMean(Person("ANDERSON", "ANNA", "9"), "DE J*NG", "ANNA")); // holds a word of the mask, is none of them: no token of the cell is theirs
        Assert.True(NameSearch.CouldMean(Person("DE JONG", "ANNA", "9"), "DE J*NG", "ANNA"));
        Assert.Equal([0], Search.Search(Person("DE JONG", "ANNA"), ["Anna De Jung"], [Person("ANDERSON", "ANNA", "9")]).Hits);

        // an exact spaced hit asks nothing of competitors, as no exact hit does; a one-word name within it finds the row
        // too — the matcher gives the row to the longer name (NAME_CONTAINED), where the workbook gave it to the shorter
        var exact = Search.Search(benAli, ["Mari Ben Ali"], [benAli, Person("ALI", "MARI", "9"), Person("BEN ALY", "MARI", "8")]);
        Assert.Equal([0], exact.Hits);
        Assert.False(exact.Ambiguous);
        Assert.Equal([0], Find(Person("ALI", "MARI", "9"), "Mari Ben Ali").Hits);

        // the whole surname span is taken before the first name is sought, on one side of it
        Assert.Empty(Find(Person("BEN ALI", "ALI"), "Ben Ali").Hits);
        Assert.Equal([0], Find(Person("BEN ALI", "ALI"), "Ali Ben Ali").Hits);
        Assert.Empty(Find(Person("BEN ALI", "ANNA MARIA"), "Anna Ben Ali Maria").Hits); // the first name's words must stand together

        // a one-letter word masked would be "*" alone, any token: such a mask means nothing
        Assert.Empty(Find(Person("LI A", "MARI"), "Mari Li Tamm").Hits);
        Assert.Empty(Find(Person("TAMM", "ANNA M"), "Anna Kask Tamm").Hits);

        // the span helper itself
        Assert.Equal((1, 2), NameSearch.SpanOf(["Mari", "Ben", "Ali"], "BEN ALI"));
        Assert.Equal((2, 1), NameSearch.SpanOf(["Mari", "Ben", "Ali"], "AL*"));
        Assert.Equal((-1, 0), NameSearch.SpanOf(["Mari", "Ben-Ali"], "BEN ALI")); // the glued spelling is the mask's, not the span's
        Assert.Equal((-1, 0), NameSearch.SpanOf(["Mari", "Ben"], "BEN ALI"));
        Assert.Equal((-1, 0), NameSearch.SpanOf(["Mari", "Ben", "Ali"], "   "));
        Assert.Equal((-1, 0), NameSearch.SpanOf(["Mari", "Ben", "Ali"], "* ALI"));
    }

    [Fact]
    public void Names_the_workbook_could_not_match_are_still_not_matched()
    {
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
