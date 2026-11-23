using SatellesTSD.Core.Lookups;
using SatellesTSD.Core.Model;
using SatellesTSD.Core.Store;
using SatellesTSD.Infrastructure;

namespace SatellesTSD.WindowTests;

/// <summary>The offer after an import (the search, with the registers' check or alone, or not now) and the start without its
/// store (the message with its ways on, in place of a bare file dialog).</summary>
public sealed class RunOfferWindowTests
{
    /// <summary>A bank statement of made-up rows that names no employer: it goes to the company shown, something new.</summary>
    private static string Statement(WindowFixture fixture, string name = "lhv.xlsx", string reference = "2026100500000009")
    {
        var path = Path.Combine(fixture.Folder, name);
        File.WriteAllBytes(path, SatellesTSD.Tests.Tables.Xlsx("LHV", [Core.Import.LayoutCatalog.Default.Find("lhv-et-short")!.Headers.ToArray<object?>(),
            ["EE000000000000000001", 1, new DateTime(2026, 10, 7), "EE12", "Kask Kati", "D", -100.0, null, reference, "palk", "EUR", null]]));
        return path;
    }

    private sealed record Asked(string Text, string Title, IReadOnlyList<string> Choices);

    private static void Run(MainForm form, Task work, string what)
    {
        Ui.Until(() => work.IsCompleted && !form.IsBusy, what: what);
        work.GetAwaiter().GetResult();
        Ui.Pump();
    }

    private static WindowFixture WithRegisters(CountingResidency residency) =>
        new() { Services = path => new AppServices(path, residency, new NotJoined(), new LookupOptions(TimeSpan.Zero)) };

    [Fact]
    public void The_offer_after_an_import_has_three_choices_and_two_without_the_registers_and_not_now_runs_nothing()
    {
        var residency = new CountingResidency();
        using (var fixture = WithRegisters(residency))
        {
            var asked = new List<Asked>();
            Ui.Run(() => fixture.Drive(form =>
            {
                form.Choosing = (text, title, choices) =>
                {
                    asked.Add(new Asked(text, title, choices));
                    return At(choices, UiText.ImportOfferNotNow);
                };
                var runBefore = form.View!.StoredRun!.Id;
                Run(form, form.ImportFilesAsync([Statement(fixture)]), "the import");
                var offer = Assert.Single(asked);
                Assert.Equal(UiText.ImportOfferTitle, offer.Title);
                Assert.Equal([UiText.ImportOfferSearchAndCheck, UiText.ImportOfferSearchOnly, UiText.ImportOfferNotNow], offer.Choices);
                Assert.Equal(UiText.ImportOffer(lookups: true, replaces: true, anythingNew: true, someRefused: false), offer.Text);
                Assert.Equal(2, form.View!.Files.Count); // the file is in
                Assert.Equal(runBefore, form.View.StoredRun!.Id); // and nothing ran
                Assert.Equal(0, residency.Asked);
            }));
        }

        using (var fixture = new WindowFixture { Services = path => new AppServices(path, null, null, null, withoutLookups: true) })
        {
            var asked = new List<Asked>();
            Ui.Run(() => fixture.Drive(form =>
            {
                Assert.False(form.Services!.Pipeline.HasLookups);
                form.Choosing = (text, title, choices) =>
                {
                    asked.Add(new Asked(text, title, choices));
                    return -1; // closed: not now
                };
                Run(form, form.ImportFilesAsync([Statement(fixture)]), "the import");
                var offer = Assert.Single(asked);
                Assert.Equal([UiText.ImportOfferSearchOnly, UiText.ImportOfferNotNow], offer.Choices);
                Assert.DoesNotContain("Pensionikeskus", offer.Text, StringComparison.Ordinal); // no word of a check it cannot do
            }));
        }
    }

    [Fact]
    public void The_search_from_the_offer_asks_nothing_more_and_shows_its_summary_once()
    {
        var residency = new CountingResidency();
        using var fixture = WithRegisters(residency);
        var questions = new List<string>();
        var summaries = new List<(bool Searched, bool Registers, LookupRunReport? Report, bool Completed)>();
        Ui.Run(() => fixture.Drive(form =>
        {
            form.RunSummaryShown = (s, r, report, c) => summaries.Add((s, r, report, c));
            form.Choosing = (_, _, choices) => At(choices, UiText.ImportOfferSearchOnly);
            var runBefore = form.View!.StoredRun!.Id;
            Run(form, form.ImportFilesAsync([Statement(fixture)]), "the import and the search");
            Assert.NotEqual(runBefore, form.View!.StoredRun!.Id); // searched afresh
            Assert.DoesNotContain(UiText.RunReplacesRun, questions); // the offer said the result is replaced
            Assert.Equal(0, residency.Asked);
            Assert.Equal((true, false, (LookupRunReport?)null, true), Assert.Single(summaries));

            // the menu's own search still asks first
            form.MatchButton.PerformClick();
            Ui.Until(() => !form.IsBusy, what: "the menu's search");
            Ui.Pump();
            Assert.Contains(UiText.RunReplacesRun, questions);
            Assert.Equal(2, summaries.Count);
        }, confirm: (question, _) => { questions.Add(question); return true; }));
    }

    [Fact]
    public void The_search_and_the_check_from_the_offer_ask_nothing_more_and_show_one_summary_after_the_check()
    {
        var residency = new CountingResidency();
        using var fixture = WithRegisters(residency);
        var questions = new List<string>();
        var summaries = new List<(bool Searched, bool Registers, LookupRunReport? Report, bool Completed)>();
        Ui.Run(() => fixture.Drive(form =>
        {
            form.RunSummaryShown = (s, r, report, c) => summaries.Add((s, r, report, c));
            form.Choosing = (_, _, choices) => At(choices, UiText.ImportOfferSearchAndCheck);
            var runBefore = form.View!.StoredRun!.Id;
            Run(form, form.ImportFilesAsync([Statement(fixture)]), "the import, the search and the check");
            Assert.NotEqual(runBefore, form.View!.StoredRun!.Id);
            Assert.True(residency.Asked > 0); // the registers were asked
            Assert.DoesNotContain(UiText.RunReplacesRun, questions);
            Assert.DoesNotContain(UiText.LookupsLive, questions);
            var summary = Assert.Single(summaries); // one, after the check
            Assert.True(summary.Searched && summary.Registers && summary.Completed);
            Assert.NotNull(summary.Report);

            // the menu's own check still asks first
            form.LookupButton.PerformClick();
            Ui.Until(() => !form.IsBusy, what: "the menu's check");
            Ui.Pump();
            Assert.Contains(UiText.LookupsLive, questions);
            Assert.Equal((false, true), (summaries[^1].Searched, summaries[^1].Registers));
        }, confirm: (question, _) => { questions.Add(question); return true; }));
    }

    [Fact]
    public void A_search_from_the_offer_that_fails_is_followed_by_no_check_and_no_summary()
    {
        var residency = new CountingResidency();
        using var fixture = WithRegisters(residency);
        var summaries = 0;
        Ui.Run(() => fixture.Drive(form =>
        {
            form.RunSummaryShown = (_, _, _, _) => summaries++;
            form.Choosing = (_, _, choices) =>
            {
                // the month is confirmed behind the window's back: the search is refused
                form.Services!.Store.SetPeriodState(form.View!.Period.Id, PeriodState.Confirmed, DateTime.UtcNow);
                return At(choices, UiText.ImportOfferSearchAndCheck);
            };
            Run(form, form.ImportFilesAsync([Statement(fixture)]), "the import and the refused search");
            Assert.Single(fixture.Failures); // the refusal said
            fixture.Failures.Clear();
            Assert.Equal(0, residency.Asked);
            Assert.Equal(0, summaries);
        }));
    }

    [Fact]
    public void No_offer_when_nothing_came_in_nothing_new_came_into_a_searched_month_or_the_month_is_locked()
    {
        var residency = new CountingResidency();
        using (var fixture = WithRegisters(residency))
        {
            var offers = 0;
            Ui.Run(() => fixture.Drive(form =>
            {
                form.Choosing = (_, _, _) => { offers++; return -1; };
                var statement = Statement(fixture);
                Run(form, form.ImportFilesAsync([statement]), "the first import");
                Assert.Equal(1, offers);
                Run(form, form.ImportFilesAsync([statement]), "the same file again");
                Assert.Equal(1, offers); // already in, and the month is searched

                // a file the store holds for another month is new to this one: the month's search has not seen it, so the
                // offer is made, and says a file came in
                var other = Statement(fixture, "teine.xlsx", "2026100500000010");
                var november = form.Services!.Store.GetOrCreatePeriod(form.View!.Period.CompanyId, 2026, 11);
                Assert.True(form.Services.Pipeline.ImportFile(november.Id, "teine.xlsx", File.ReadAllBytes(other)).Succeeded);
                string? asked = null;
                form.Choosing = (text, _, _) => { offers++; asked = text; return -1; };
                Run(form, form.ImportFilesAsync([other]), "the other month's file");
                Assert.Equal(2, offers);
                Assert.StartsWith("Failid on imporditud.", asked, StringComparison.Ordinal);
                Run(form, form.ImportFilesAsync([other]), "the other month's file again");
                Assert.Equal(2, offers); // listed here now: nothing new to offer for (the state line says the search is out of date)
            }));
        }

        // a month with no search yet: nothing in is no offer; a file already in is offered all the same, the search not made
        using (var fixture = new WindowFixture(prepare: (store, _, period, _) => store.ClearMatchRun(period.Id))
            { Services = path => new AppServices(path, residency, new NotJoined(), new LookupOptions(TimeSpan.Zero)) })
        {
            var offers = 0;
            Ui.Run(() => fixture.Drive(form =>
            {
                Assert.Null(form.View!.Run);
                form.Choosing = (_, _, _) => { offers++; return -1; };
                var junk = Path.Combine(fixture.Folder, "muu.csv");
                File.WriteAllText(junk, "see ei ole väljavõte\n");
                Run(form, form.ImportFilesAsync([junk]), "the refused import");
                Assert.Equal(0, offers);

                var statement = Statement(fixture);
                Run(form, form.ImportFilesAsync([statement]), "the first import");
                Assert.Equal(1, offers);
                Run(form, form.ImportFilesAsync([statement]), "the same file again");
                Assert.Equal(2, offers);
            }));
        }

        using (var fixture = new WindowFixture(prepare: (store, _, period, _) => store.SetPeriodState(period.Id, PeriodState.Confirmed, DateTime.UtcNow))
            { Services = path => new AppServices(path, residency, new NotJoined(), new LookupOptions(TimeSpan.Zero)) })
        {
            var offers = 0;
            Ui.Run(() => fixture.Drive(form =>
            {
                form.Informing = _ => { };
                form.Choosing = (_, _, _) => { offers++; return -1; };
                Run(form, form.ImportFilesAsync([Statement(fixture)]), "the import into a locked month");
                Assert.Equal(0, offers);
            }));
        }

        Assert.Equal(0, residency.Asked);
    }

    // ---------------------------------------------------------------- the start without its store

    /// <summary>A folder with a settings file naming a store, or none; the window opened on it with the test's answers.</summary>
    private sealed class Start : IDisposable
    {
        public Start(bool named = true)
        {
            Folder = Path.Combine(Path.GetTempPath(), $"satellestsd-start-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Folder);
            Missing = Path.Combine(Folder, "kadunud.db");
            SettingsPath = Path.Combine(Folder, "settings.json");
            new AppSettings { StorePath = named ? Missing : null, LastYear = 2026, LastMonth = 10 }.Save(SettingsPath);
        }

        public string Folder { get; }

        public string Missing { get; }

        public string SettingsPath { get; }

        public List<Asked> Asked { get; } = [];

        public List<bool> FileDialogs { get; } = [];

        public List<Exception> Failures { get; } = [];

        public string MakeStore(string name)
        {
            var path = Path.Combine(Folder, name);
            using (SqliteStore.Open(path))
            {
            }

            return path;
        }

        /// <summary>Opens the window, hands it to the body, closes it; the answers are the test's.</summary>
        public void Drive(Func<Asked, int> choose, Func<bool, string?> file, Action<MainForm> body, string? storeOverride = null)
        {
            var form = new MainForm(SettingsPath, AppSettings.Load(SettingsPath), storeOverride)
            {
                StartPosition = FormStartPosition.Manual,
                Location = new Point(-4000, -4000),
                Confirming = (_, _) => true,
                Choosing = (text, title, choices) =>
                {
                    var asked = new Asked(text, title, choices);
                    Asked.Add(asked);
                    return Asked.Count > 5 ? -1 : choose(asked); // a message put again and again ends, and the test's count says it
                },
                ChoosingStoreFile = create =>
                {
                    FileDialogs.Add(create);
                    return file(create);
                },
                ServicesFactory = path => new AppServices(path, null, null, null, withoutLookups: true),
            };
            form.Failing = Failures.Add;
            try
            {
                form.Show();
                Ui.Until(() => !form.IsBusy, what: "the window to go idle after opening");
                body(form);
            }
            finally
            {
                form.Close();
                Ui.Pump();
                form.Dispose();
            }

            Assert.Empty(Failures);
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Folder, true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private static int At(IReadOnlyList<string> choices, string choice) => choices.ToList().IndexOf(choice);

    private static int Index(Asked asked, string choice)
    {
        var index = At(asked.Choices, choice);
        Assert.True(index >= 0, $"no choice \"{choice}\" among {string.Join(", ", asked.Choices)}");
        return index;
    }

    private static int Logged(MainForm form, string text) => form.LogList.Items.Cast<object>().Count(i => i.ToString()!.Contains(text, StringComparison.Ordinal));

    [Fact]
    public void A_first_start_with_no_store_named_says_so_and_offers_to_open_create_or_go_on_without()
    {
        using var start = new Start(named: false);
        var existing = start.MakeStore("olemas.db");
        var answers = new Queue<string?>([null, existing]); // the first file dialog cancelled, then a file named
        start.Drive(asked => At(asked.Choices, UiText.StoreOpenChoice), _ => answers.Dequeue(), form =>
        {
            Assert.Equal(2, start.Asked.Count); // the cancelled dialog came back to the message
            Assert.All(start.Asked, a => Assert.Equal((UiText.NoStoreChosen, UiText.StoreMissingTitle), (a.Text, a.Title)));
            Assert.Equal([UiText.StoreOpenChoice, UiText.StoreCreate, UiText.StoreContinueWithout], start.Asked[0].Choices);
            Assert.Equal([false, false], start.FileDialogs);
            Assert.Equal(existing, form.Services!.StorePath);
            Assert.Equal(0, Logged(form, UiText.StoreMissing)); // nothing is missing: none was named
        });
        Assert.Equal(existing, AppSettings.Load(start.SettingsPath).StorePath); // chosen, remembered
    }

    [Fact]
    public void A_store_not_found_says_where_and_why_and_try_again_opens_it_once_it_is_there()
    {
        using var start = new Start();
        start.Drive(asked =>
        {
            if (start.Asked.Count == 2)
            {
                start.MakeStore("kadunud.db"); // the network drive came up meanwhile
            }

            return Index(asked, UiText.StoreTryAgain);
        }, _ => throw new InvalidOperationException("no file dialog"), form =>
        {
            Assert.Equal(2, start.Asked.Count);
            Assert.Equal(UiText.StoreNotFound(start.Missing, again: false), start.Asked[0].Text);
            Assert.Contains(start.Missing, start.Asked[0].Text, StringComparison.Ordinal);
            Assert.Contains("S:", start.Asked[0].Text, StringComparison.Ordinal);
            Assert.Equal([UiText.StoreTryAgain, UiText.StoreFind, UiText.StoreCreate, UiText.StoreContinueWithout], start.Asked[0].Choices);
            Assert.Equal(UiText.StoreNotFound(start.Missing, again: true), start.Asked[1].Text); // still not there, said so
            Assert.NotEqual(start.Asked[0].Text, start.Asked[1].Text);
            Assert.Equal(start.Missing, form.Services!.StorePath);
            Assert.Equal(1, Logged(form, UiText.StoreMissing)); // logged once, however many times it was tried
        });
        Assert.Empty(start.FileDialogs);
        Assert.Equal(start.Missing, AppSettings.Load(start.SettingsPath).StorePath);
    }

    [Fact]
    public void A_store_not_found_is_found_elsewhere_or_a_new_one_created_and_a_cancelled_dialog_comes_back_to_the_message()
    {
        using (var start = new Start())
        {
            var elsewhere = start.MakeStore("teisaldatud.db");
            var files = new Queue<string?>([null, elsewhere]);
            start.Drive(asked => Index(asked, UiText.StoreFind), _ => files.Dequeue(), form =>
            {
                Assert.Equal(2, start.Asked.Count); // cancelled once: the message again
                Assert.Equal([false, false], start.FileDialogs);
                Assert.Equal(elsewhere, form.Services!.StorePath);
            });
            Assert.Equal(elsewhere, AppSettings.Load(start.SettingsPath).StorePath);
        }

        using (var start = new Start())
        {
            var created = Path.Combine(start.Folder, "uus.db");
            start.Drive(asked => Index(asked, UiText.StoreCreate), _ => created, form =>
            {
                Assert.Single(start.Asked);
                Assert.Equal([true], start.FileDialogs);
                Assert.Equal(created, form.Services!.StorePath);
                Assert.True(File.Exists(created));
            });
            Assert.Equal(created, AppSettings.Load(start.SettingsPath).StorePath);
        }
    }

    [Fact]
    public void Going_on_without_a_store_leaves_the_window_with_none_and_the_remembered_path_as_it_was()
    {
        foreach (var answer in new Func<Asked, int>[] { asked => Index(asked, UiText.StoreContinueWithout), _ => -1 })
        {
            using var start = new Start();
            start.Drive(answer, _ => throw new InvalidOperationException("no file dialog"), form =>
            {
                Assert.Single(start.Asked);
                Assert.Null(form.Services);
                Assert.True(form.OpenStoreButton.Enabled); // the Andmebaas menu still opens one
            });
            Assert.Empty(start.FileDialogs);
            Assert.Equal(start.Missing, AppSettings.Load(start.SettingsPath).StorePath); // the next start looks for the same file
            Assert.False(File.Exists(start.Missing)); // and no empty store was made in its place
        }
    }

    [Fact]
    public void A_one_off_store_named_on_the_command_line_that_is_missing_is_said_the_same_way()
    {
        using var start = new Start(named: false);
        var remembered = start.MakeStore("tavaline.db");
        new AppSettings { StorePath = remembered, LastYear = 2026, LastMonth = 10 }.Save(start.SettingsPath);
        var oneOff = Path.Combine(start.Folder, "ühekordne.db");
        start.Drive(asked => Index(asked, UiText.StoreContinueWithout), _ => null, form =>
        {
            Assert.Equal(UiText.StoreNotFound(oneOff, again: false), Assert.Single(start.Asked).Text);
            Assert.Null(form.Services);
        }, storeOverride: oneOff);
        Assert.Equal(remembered, AppSettings.Load(start.SettingsPath).StorePath);

        // tried again once it is there: opened as the start would have, a one-off still — the remembered store stays remembered
        start.Asked.Clear();
        start.Drive(_ =>
        {
            start.MakeStore("ühekordne.db");
            return Index(start.Asked[^1], UiText.StoreTryAgain);
        }, _ => throw new InvalidOperationException("no file dialog"), form =>
        {
            Assert.Single(start.Asked);
            Assert.Equal(oneOff, form.Services!.StorePath);
        }, storeOverride: oneOff);
        Assert.Equal(remembered, AppSettings.Load(start.SettingsPath).StorePath);
    }

    [Fact]
    public void A_file_that_will_not_open_brings_the_question_back_and_try_again_says_it_is_there_but_would_not_open()
    {
        // the first start: a file chosen that is no store, then the right one
        using (var start = new Start(named: false))
        {
            var junk = Path.Combine(start.Folder, "vale.db");
            File.WriteAllText(junk, "see ei ole andmebaas, vaid tavaline tekst, mida SQLite ei loe\n");
            var existing = start.MakeStore("olemas.db");
            var files = new Queue<string?>([junk, existing]);
            start.Drive(asked => At(asked.Choices, UiText.StoreOpenChoice), _ => files.Dequeue(), form =>
            {
                Assert.Equal(2, start.Asked.Count); // the error said, then the question again
                Assert.Single(start.Failures);
                start.Failures.Clear();
                Assert.Equal(existing, form.Services!.StorePath);
            });
            Assert.Equal(existing, AppSettings.Load(start.SettingsPath).StorePath);
        }

        // the store named comes back, but not as a store: said so, then gone on without
        using (var start = new Start())
        {
            start.Drive(asked =>
            {
                if (start.Asked.Count == 1)
                {
                    File.WriteAllText(start.Missing, "see ei ole andmebaas, vaid tavaline tekst, mida SQLite ei loe\n");
                    return Index(asked, UiText.StoreTryAgain);
                }

                return Index(asked, UiText.StoreContinueWithout);
            }, _ => throw new InvalidOperationException("no file dialog"), form =>
            {
                Assert.Equal(2, start.Asked.Count);
                Assert.Equal(UiText.StoreNotOpened(start.Missing), start.Asked[1].Text);
                Assert.Equal([UiText.StoreTryAgain, UiText.StoreFind, UiText.StoreCreate, UiText.StoreContinueWithout], start.Asked[1].Choices);
                Assert.Single(start.Failures);
                start.Failures.Clear();
                Assert.Null(form.Services);
            });
            Assert.Equal(start.Missing, AppSettings.Load(start.SettingsPath).StorePath);
        }
    }

    [Fact]
    public void The_offers_first_sentence_says_what_the_import_did()
    {
        Assert.StartsWith("Failid on imporditud.", UiText.ImportOffer(true, true, anythingNew: true, someRefused: false), StringComparison.Ordinal);
        Assert.StartsWith("Osa faile on imporditud, osa jäi importimata", UiText.ImportOffer(true, true, anythingNew: true, someRefused: true), StringComparison.Ordinal);
        Assert.StartsWith("Failid olid juba imporditud, kuid kuu makseid pole veel otsitud.", UiText.ImportOffer(true, false, anythingNew: false, someRefused: false), StringComparison.Ordinal);
        Assert.StartsWith("Uusi faile ei imporditud", UiText.ImportOffer(true, false, anythingNew: false, someRefused: true), StringComparison.Ordinal);

        var residency = new CountingResidency();
        using (var fixture = WithRegisters(residency))
        {
            var asked = new List<string>();
            Ui.Run(() => fixture.Drive(form =>
            {
                form.Choosing = (text, _, _) => { asked.Add(text); return -1; };
                var junk = Path.Combine(fixture.Folder, "muu.csv");
                File.WriteAllText(junk, "see ei ole väljavõte\n");
                Run(form, form.ImportFilesAsync([Statement(fixture), junk]), "one file in, one refused");
                Assert.Equal(UiText.ImportOffer(lookups: true, replaces: true, anythingNew: true, someRefused: true), Assert.Single(asked));
            }));
        }

        // all there before, in a month not yet searched
        using (var fixture = new WindowFixture(prepare: (store, _, period, _) => store.ClearMatchRun(period.Id))
            { Services = path => new AppServices(path, residency, new NotJoined(), new LookupOptions(TimeSpan.Zero)) })
        {
            var asked = new List<string>();
            Ui.Run(() => fixture.Drive(form =>
            {
                form.Choosing = (text, _, _) => { asked.Add(text); return -1; };
                var statement = Statement(fixture);
                Run(form, form.ImportFilesAsync([statement]), "the first import");
                Run(form, form.ImportFilesAsync([statement]), "the same file again");
                Assert.Equal(2, asked.Count);
                Assert.Equal(UiText.ImportOffer(lookups: true, replaces: false, anythingNew: true, someRefused: false), asked[0]);
                Assert.Equal(UiText.ImportOffer(lookups: true, replaces: false, anythingNew: false, someRefused: false), asked[1]);
            }));
        }
    }

    private sealed class CountingResidency : IResidencyLookup
    {
        private int _asked;

        public int Asked => _asked;

        public async Task<LookupResult<ResidencyFinding>> QueryAsync(string personalCode, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _asked);
            await Task.Delay(1, cancellationToken);
            return LookupResult<ResidencyFinding>.Found(new ResidencyFinding(new DateOnly(2026, 11, 4), [], []), null);
        }
    }

    private sealed class NotJoined : IPensionLookup
    {
        public Task<LookupResult<PensionFinding>> QueryAsync(string personalCode, CancellationToken cancellationToken = default) =>
            Task.FromResult(LookupResult<PensionFinding>.Found(new PensionFinding(PensionStatus.NotJoined, null, null, []), null));
    }
}
