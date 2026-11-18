using System.Globalization;
using System.Text;
using SatellesTSD.Core.Declare;
using SatellesTSD.Core.Matching;
using SatellesTSD.Core.Model;
using SatellesTSD.Core.Review;
using SatellesTSD.Core.Store;
using SatellesTSD.Core.Tax;
using SatellesTSD.Infrastructure;

namespace SatellesTSD;

/// <summary>The simulation (decision 204): one example person — the card's facts, the register as it stands for them,
/// the month's bank rows with the review's word on each — run through the app's own import, search, review and build
/// on a store of its own (<see cref="Simulator"/>); the lines, the review and the file's XML shown. A scenario is saved
/// to a file and opened from one. Nothing touches the store in use.</summary>
public sealed class SimulationDialog : Form
{
    private static readonly CultureInfo Et = CultureInfo.GetCultureInfo("et-EE");
    private static readonly string[] DayFormats = ["d.M.yyyy", "dd.MM.yyyy", "yyyy-MM-dd"];

    private readonly string? _schemaRoot;
    private readonly string? _savedCatalog;
    private readonly TwoContractsRule _startRule;
    private readonly ToolTip _tips = new();
    private readonly NumericUpDown _year = new() { Minimum = 2000, Maximum = 2100, Width = 70 };
    private readonly NumericUpDown _month = new() { Minimum = 1, Maximum = 12, Width = 50 };
    private readonly ComboBox _register = Combo([new Choice(nameof(SimRegister.Listed), UiText.SimListed), new Choice(nameof(SimRegister.NotListed), UiText.SimNotListed), new Choice(nameof(SimRegister.NoRegister), UiText.SimNoRegister)], 260);
    private readonly ComboBox _mode = Combo([new Choice(nameof(DeclarationMode.PerPayment), UiText.DeclareEachPayment), new Choice(nameof(DeclarationMode.TotalsPerType), UiText.DeclareTotalsPerType)], 380);
    private readonly ComboBox _twoContracts = Combo([.. Enum.GetValues<TwoContractsRule>().Select(r => new Choice(r.ToString(), UiText.TwoContractsText(r)))], 240);

    private readonly TextBox _first = new() { Width = 160 };
    private readonly TextBox _last = new() { Width = 160 };
    private readonly TextBox _code = new() { Width = 160 };
    private readonly TextBox _mrr = new() { Width = 160 };
    private readonly ComboBox _residency = Combo([new Choice("R", UiText.Resident), new Choice("N", UiText.NonResident)], 160);
    private readonly TextBox _country = new() { Width = 60 };
    private readonly ComboBox _member = Combo([new Choice("1", UiText.Yes), new Choice("0", UiText.No), new Choice("", UiText.SimUnchecked)], 160);
    private readonly ComboBox _rate = Combo([new Choice("0.02", "2 %"), new Choice("0.04", "4 %"), new Choice("0.06", "6 %")], 160);
    private readonly CheckBox _onPension = new() { Text = UiText.OnPension, AutoSize = true };
    private readonly TextBox _pensionAgeYear = new() { Width = 60 };
    private readonly CheckBox _exemption = new() { Text = UiText.ExemptionApplied, AutoSize = true, Checked = true };
    private readonly TextBox _exemptionMonthly = new() { Width = 80 };
    private readonly CheckBox _minimum = new() { Text = UiText.SocialTaxMinimum, AutoSize = true, Checked = true };
    private readonly ComboBox _a1 = Combo([new Choice(nameof(A1Case.None), UiText.RulesA1None), new Choice(nameof(A1Case.EstonianA1WorkAbroad), UiText.RulesA1Estonian), new Choice(nameof(A1Case.ForeignA1WorkInEstonia), UiText.RulesA1Foreign)], 200);
    private readonly TextBox _a1Country = new() { Width = 60 };
    private readonly TextBox _workCountry = new() { Width = 60 };
    private readonly TextBox _treaty = new() { Width = 60 };
    private readonly ComboBox _torType = Combo([new Choice("", UiText.SimNoCardType), .. EmploymentTypes.All.Select(e => new Choice(e.TorTypeId, $"{e.TorTypeId} {e.Name}"))], 220);
    private readonly CheckBox _typeFromCard = new() { Text = UiText.SimTypeFromCard, AutoSize = true };
    private readonly ComboBox _declaration = Combo([new Choice(nameof(DeclarationChoice.Auto), UiText.DeclarationAuto), new Choice(nameof(DeclarationChoice.Include), UiText.DeclarationInclude), new Choice(nameof(DeclarationChoice.Exclude), UiText.DeclarationExclude)], 160);

    private readonly DataGridView _employments = EditGrid();
    private readonly DataGridView _rows = EditGrid();
    private readonly DataGridView _lines = ReadGrid();
    private readonly TextBox _review = Output();
    private readonly TextBox _xml = Output();
    private readonly TabControl _results = new() { Dock = DockStyle.Fill };
    private readonly Button _run = new() { Text = UiText.SimRun, AutoSize = true };
    private readonly Button _open = new() { Text = UiText.SimOpen, AutoSize = true };
    private readonly Button _save = new() { Text = UiText.SimSave, AutoSize = true };

    /// <param name="savedCatalog">The store in use's saved catalog document (the owner's phrases), so the example's rows are
    /// found as the app's own month finds them; null = the built-in phrases.</param>
    /// <param name="twoContracts">The window's two-contracts rule (decision 209), the example's to start with.</param>
    public SimulationDialog(int year, int month, string? schemaRoot = null, string? savedCatalog = null, TwoContractsRule twoContracts = TwoContractsRule.SalaryToEmploymentContract)
    {
        _startRule = twoContracts;
        _schemaRoot = schemaRoot;
        _savedCatalog = savedCatalog;
        Text = UiText.SimTitle;
        Size = new Size(1360, 880);
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        MinimizeBox = false;

        var top = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(4) };
        top.Controls.AddRange([Caption(UiText.SimMonth), _year, _month, Caption(UiText.SimRegisterLabel), _register, Caption(UiText.SimCut), _mode, Caption(UiText.TwoContractsMenu), _twoContracts]);
        _tips.SetToolTip(_twoContracts, UiText.TwoContractsMenuTip);

        var card = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, AutoScroll = true, Padding = new Padding(4) };
        void Field(string label, Control control)
        {
            card.Controls.Add(new Label { Text = label, AutoSize = true, Padding = new Padding(0, 6, 0, 0) });
            card.Controls.Add(control);
        }

        Field(UiText.SimFirstName, _first);
        Field(UiText.SimLastName, _last);
        Field(UiText.SimCode, _code);
        Field(UiText.SimMrr, _mrr);
        Field(UiText.Residency, _residency);
        Field(UiText.SimCountry, _country);
        Field(UiText.PensionMemberLabel, _member);
        Field(UiText.PensionRate, _rate);
        Field("", _onPension);
        Field(UiText.SimPensionAgeYear, _pensionAgeYear);
        Field("", _exemption);
        Field(UiText.ExemptionMonthly, _exemptionMonthly);
        Field("", _minimum);
        Field("A1", _a1);
        Field(UiText.SimA1Country, _a1Country);
        Field(UiText.SimWorkCountry, _workCountry);
        Field(UiText.SimTreaty, _treaty);
        Field(UiText.SimCardType, _torType);
        Field("", _typeFromCard);
        Field(UiText.DeclarationLabel, _declaration);
        var cardBox = new GroupBox { Text = UiText.SimCardTitle, Dock = DockStyle.Fill };
        cardBox.Controls.Add(card);

        _employments.Columns.Add(new DataGridViewComboBoxColumn
        {
            Name = "type", HeaderText = UiText.RulesTor, Width = 230, FlatStyle = FlatStyle.Flat,
            DataSource = EmploymentTypes.All.Select(e => new Choice(e.TorTypeId, $"{e.TorTypeId} {e.Name}")).ToList(),
            ValueMember = nameof(Choice.Value), DisplayMember = nameof(Choice.Text),
        });
        foreach (var (name, header) in new[] { ("start", UiText.SimStart), ("end", UiText.SimEnd), ("pauseStart", UiText.SimPauseStart), ("pauseEnd", UiText.SimPauseEnd) })
        {
            _employments.Columns.Add(new DataGridViewTextBoxColumn { Name = name, HeaderText = header, Width = 110 });
        }

        foreach (var (name, header, width) in new[] { ("date", UiText.SimDate, 90), ("payee", UiText.SimPayee, 170), ("amount", UiText.Amount, 90), ("description", UiText.SimDescription, 200), ("currency", UiText.SimCurrency, 60) })
        {
            _rows.Columns.Add(new DataGridViewTextBoxColumn { Name = name, HeaderText = header, Width = width });
        }

        _rows.Columns.Add(new DataGridViewCheckBoxColumn { Name = "in", HeaderText = UiText.SimMoneyIn, Width = 70 });
        _rows.Columns.Add(new DataGridViewComboBoxColumn
        {
            Name = "category", HeaderText = UiText.PhrasesCategory, Width = 160, FlatStyle = FlatStyle.Flat,
            DataSource = new[] { new Choice("", UiText.SimBySearch) }.Concat(MatchCatalog.Default.Categories.Select(c => new Choice(c.Name, c.Name))).ToList(),
            ValueMember = nameof(Choice.Value), DisplayMember = nameof(Choice.Text),
        });
        _rows.Columns.Add(new DataGridViewTextBoxColumn { Name = "type", HeaderText = UiText.SimTypeCode, Width = 60 });
        _rows.Columns.Add(new DataGridViewComboBoxColumn
        {
            Name = "declare", HeaderText = UiText.SimDeclare, Width = 110, FlatStyle = FlatStyle.Flat,
            DataSource = new[] { new Choice("", UiText.SimBySearch), new Choice("1", UiText.Yes), new Choice("0", UiText.No) }.ToList(),
            ValueMember = nameof(Choice.Value), DisplayMember = nameof(Choice.Text),
        });

        var employmentsBox = new GroupBox { Text = UiText.SimEmploymentsTitle, Dock = DockStyle.Fill };
        employmentsBox.Controls.Add(_employments);
        var rowsBox = new GroupBox { Text = UiText.SimRowsTitle, Dock = DockStyle.Fill };
        rowsBox.Controls.Add(_rows);
        var right = new SplitContainer { Name = "right", Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterDistance = 150 };
        right.Panel1.Controls.Add(employmentsBox);
        right.Panel2.Controls.Add(rowsBox);
        var inputs = new SplitContainer { Name = "inputs", Dock = DockStyle.Fill, SplitterDistance = 430 };
        inputs.Panel1.Controls.Add(cardBox);
        inputs.Panel2.Controls.Add(right);

        foreach (var (name, header, width) in new[]
        {
            ("key", UiText.SimLineKey, 70), ("action", UiText.SimAction, 50), ("type", UiText.RulesCode, 50), ("date", UiText.SimDate, 85),
            ("net", UiText.SimNet, 85), ("gross", UiText.SimGross, 85), ("tm", "TM", 75), ("mvt", UiText.RulesExemption, 75), ("sm", "SM", 75),
            ("increase", UiText.SimIncrease, 85), ("tk", "TK", 65), ("ttk", "TTK", 65), ("kp", "KP", 65), ("source", UiText.SimSource, 260),
        })
        {
            _lines.Columns.Add(new DataGridViewTextBoxColumn { Name = name, HeaderText = header, Width = width, SortMode = DataGridViewColumnSortMode.NotSortable });
        }

        AddTab(UiText.SimLinesTab, _lines);
        AddTab(UiText.SimReviewTab, _review);
        AddTab(UiText.SimXmlTab, _xml);

        var split = new SplitContainer { Name = "main", Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterDistance = 470 };
        split.Panel1.Controls.Add(inputs);
        split.Panel2.Controls.Add(_results);
        var close = new Button { Text = UiText.Ok, DialogResult = DialogResult.OK, AutoSize = true };
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft };
        buttons.Controls.AddRange([close, _save, _open, _run]);
        Controls.Add(split);
        Controls.Add(top);
        Controls.Add(buttons);
        CancelButton = close;
        // a value a combo cell does not offer (a hand-edited file) is left blank rather than a crash box
        _employments.DataError += (_, e) => e.ThrowException = false;
        _rows.DataError += (_, e) => e.ThrowException = false;
        _run.Click += (_, _) => RunNow();
        _save.Click += (_, _) => SaveScenario();
        _open.Click += (_, _) => OpenScenario();
        _exemption.CheckedChanged += (_, _) => _exemptionMonthly.Enabled = _exemption.Checked;
        _member.SelectedIndexChanged += (_, _) => _rate.Enabled = ((Choice)_member.SelectedItem!).Value == "1";

        Fill(Example(year, month) with { TwoContracts = _startRule });
    }

    internal NumericUpDown YearBox => _year;
    internal NumericUpDown MonthBox => _month;
    internal ComboBox RegisterBox => _register;
    internal ComboBox ModeBox => _mode;

    internal ComboBox TwoContractsBox => _twoContracts;
    internal TextBox FirstNameBox => _first;
    internal TextBox LastNameBox => _last;
    internal TextBox CodeBox => _code;
    internal ComboBox ResidencyBox => _residency;
    internal ComboBox PensionMemberBox => _member;
    internal CheckBox MinimumBox => _minimum;
    internal TextBox PensionAgeYearBox => _pensionAgeYear;
    internal CheckBox ExemptionBox => _exemption;
    internal ComboBox CardTypeBox => _torType;
    internal DataGridView EmploymentsGrid => _employments;
    internal DataGridView RowsGrid => _rows;
    internal DataGridView LinesGrid => _lines;
    internal string ReviewText => _review.Text;
    internal string XmlText => _xml.Text;
    internal Button RunButton => _run;
    internal Button OpenButton => _open;
    internal Button SaveButton => _save;

    /// <summary>The last run's result; null before a run.</summary>
    internal SimResult? Result { get; private set; }

    /// <summary>The file to save the scenario to, given the name offered; null = cancelled. A save dialog by default.</summary>
    internal Func<string, string?>? ChoosingFile;

    /// <summary>The file to open a scenario from; null = cancelled. An open dialog by default.</summary>
    internal Func<string?>? OpeningFile;

    /// <summary>A refusal shown. A message box by default.</summary>
    internal Action<string>? Warning;

    /// <summary>A made-up personal code of the example (a woman born 1.1.1990, the check digit right).</summary>
    internal const string ExampleCode = "49001010012";

    /// <summary>The example the dialog opens with: a resident on a contract of employment, one salary in the month.</summary>
    internal static SimScenario Example(int year, int month) => new(
        year, month,
        new SimPerson("Näidis", "Töötaja", ExampleCode, FundedPensionMember: true),
        SimRegister.Listed,
        [new SimEmployment("1", new DateOnly(year - 1, 1, 1))],
        [new SimRow(new DateOnly(year, month, Math.Min(5, DateTime.DaysInMonth(year, month))), "Näidis Töötaja", 1000m, "palk")]);

    /// <summary>The scenario the fields and grids say; null with the refusal shown when a field cannot be read.</summary>
    internal SimScenario? Scenario()
    {
        var problems = new List<string>();
        var year = (int)_year.Value;
        var month = (int)_month.Value;
        string? Text(TextBox box) => string.IsNullOrWhiteSpace(box.Text) ? null : box.Text.Trim();
        decimal? Number(TextBox box, string label)
        {
            if (Text(box) is not { } text)
            {
                return null;
            }

            if (ParseNumber(text, out var value))
            {
                return value;
            }

            problems.Add(UiText.SimNotANumber(label, text));
            return null;
        }

        if (Text(_first) is null || Text(_last) is null)
        {
            problems.Add(UiText.SimNameMissing);
        }

        var member = ((Choice)_member.SelectedItem!).Value;
        int? pensionAgeYear = null;
        if (Text(_pensionAgeYear) is { } yearText)
        {
            if (int.TryParse(yearText, NumberStyles.None, CultureInfo.InvariantCulture, out var y) && y is >= 1900 and <= 2200)
            {
                pensionAgeYear = y;
            }
            else
            {
                problems.Add(UiText.SimPensionYearInvalid);
            }
        }

        var treaty = Number(_treaty, UiText.SimTreaty);
        var person = new SimPerson(
            Text(_first) ?? "",
            Text(_last) ?? "",
            Text(_code),
            ((Choice)_residency.SelectedItem!).Value == "N" ? Residency.NonResident : Residency.Resident,
            Text(_country),
            Text(_mrr),
            member == "" ? null : member == "1",
            member == "1" ? decimal.Parse(((Choice)_rate.SelectedItem!).Value, CultureInfo.InvariantCulture) : null,
            _onPension.Checked,
            pensionAgeYear,
            _exemption.Checked,
            _exemption.Checked ? Number(_exemptionMonthly, UiText.ExemptionMonthly) : null,
            _minimum.Checked,
            Enum.Parse<A1Case>(((Choice)_a1.SelectedItem!).Value),
            Text(_a1Country),
            Text(_workCountry),
            treaty is { } percent ? percent / 100m : null,
            ((Choice)_torType.SelectedItem!).Value is { Length: > 0 } cardType ? cardType : null,
            _typeFromCard.Checked,
            Enum.Parse<DeclarationChoice>(((Choice)_declaration.SelectedItem!).Value));

        var employments = new List<SimEmployment>();
        foreach (DataGridViewRow row in _employments.Rows)
        {
            if (row.IsNewRow)
            {
                continue;
            }

            var type = row.Cells["type"].Value as string;
            var said = problems.Count;
            var start = Day(row.Cells["start"].Value, UiText.SimStart, problems);
            if (string.IsNullOrWhiteSpace(type) || start is null)
            {
                if (problems.Count == said)
                {
                    problems.Add(UiText.SimEmploymentIncomplete(row.Index + 1));
                }

                continue;
            }

            employments.Add(new SimEmployment(type, start.Value, Day(row.Cells["end"].Value, UiText.SimEnd, problems), Day(row.Cells["pauseStart"].Value, UiText.SimPauseStart, problems), Day(row.Cells["pauseEnd"].Value, UiText.SimPauseEnd, problems)));
        }

        var rows = new List<SimRow>();
        foreach (DataGridViewRow row in _rows.Rows)
        {
            if (row.IsNewRow)
            {
                continue;
            }

            var said = problems.Count;
            var date = Day(row.Cells["date"].Value, UiText.SimDate, problems);
            var amountText = (row.Cells["amount"].Value as string)?.Trim();
            var read = ParseNumber(amountText, out var amount);
            if (!read && !string.IsNullOrEmpty(amountText))
            {
                // typed, but no figure: said as the card's fields say it (the fifth audit, item 28)
                problems.Add(UiText.SimNotANumber(UiText.Amount, amountText));
            }

            if (date is null || !read)
            {
                // a cell left empty is said as the row being incomplete; one that did not read was said above
                if (problems.Count == said || (date is null && string.IsNullOrWhiteSpace(row.Cells["date"].Value as string)))
                {
                    problems.Add(UiText.SimRowIncomplete(row.Index + 1));
                }

                continue;
            }

            var payee = (row.Cells["payee"].Value as string)?.Trim();
            var declare = row.Cells["declare"].Value as string;
            rows.Add(new SimRow(
                date.Value,
                string.IsNullOrEmpty(payee) ? $"{person.FirstName} {person.LastName}".Trim() : payee,
                Math.Abs(amount),
                (row.Cells["description"].Value as string)?.Trim() ?? "",
                row.Cells["in"].Value is true,
                (row.Cells["currency"].Value as string)?.Trim() is { Length: > 0 } currency ? currency : "EUR",
                row.Cells["category"].Value as string is { Length: > 0 } category ? category : null,
                (row.Cells["type"].Value as string)?.Trim() is { Length: > 0 } typeCode ? typeCode : null,
                declare is "1" ? true : declare is "0" ? false : null));
        }

        if (rows.Count == 0 && problems.Count == 0)
        {
            problems.Add(UiText.SimNoRows);
        }

        if (problems.Count > 0)
        {
            Tell(string.Join(Environment.NewLine, problems.Distinct()));
            return null;
        }

        return new SimScenario(year, month, person, Enum.Parse<SimRegister>(((Choice)_register.SelectedItem!).Value), employments, rows, Enum.Parse<DeclarationMode>(((Choice)_mode.SelectedItem!).Value), Enum.Parse<TwoContractsRule>(((Choice)_twoContracts.SelectedItem!).Value));
    }

    /// <summary>A scenario put into the fields and grids.</summary>
    internal void Fill(SimScenario scenario)
    {
        _year.Value = Math.Clamp(scenario.Year, (int)_year.Minimum, (int)_year.Maximum);
        _month.Value = Math.Clamp(scenario.Month, 1, 12);
        Select(_register, scenario.Register.ToString());
        Select(_mode, scenario.Mode.ToString());
        Select(_twoContracts, scenario.TwoContracts.ToString());
        var p = scenario.Person;
        _first.Text = p.FirstName;
        _last.Text = p.LastName;
        _code.Text = p.PersonalCode ?? "";
        _mrr.Text = p.MrrCode ?? "";
        Select(_residency, p.Residency == Residency.NonResident ? "N" : "R");
        _country.Text = p.ResidenceCountry ?? "";
        Select(_member, p.FundedPensionMember switch { true => "1", false => "0", null => "" });
        Select(_rate, (p.PensionRate ?? 0.02m).ToString("0.00", CultureInfo.InvariantCulture));
        _onPension.Checked = p.OnOldAgeOrFlexiblePension;
        _pensionAgeYear.Text = p.PensionAgeYear?.ToString(CultureInfo.InvariantCulture) ?? "";
        _exemption.Checked = p.BasicExemptionApplied;
        _exemptionMonthly.Text = p.BasicExemptionMonthly?.ToString("0.00", Et) ?? "";
        _minimum.Checked = p.SocialTaxMinimumApplies;
        Select(_a1, p.A1.ToString());
        _a1Country.Text = p.A1Country ?? "";
        _workCountry.Text = p.WorkCountry ?? "";
        _treaty.Text = p.TreatyIncomeTaxRate is { } treaty ? (treaty * 100m).ToString("0.##", Et) : "";
        Select(_torType, p.TorTypeId ?? "");
        _typeFromCard.Checked = p.TypeFromCard;
        Select(_declaration, p.Declaration.ToString());

        _employments.Rows.Clear();
        foreach (var e in scenario.Employments)
        {
            _employments.Rows.Add(EmploymentTypes.Find(e.TypeId)?.TorTypeId, DayText(e.Start), DayText(e.End), DayText(e.PauseStart), DayText(e.PauseEnd));
        }

        _rows.Rows.Clear();
        foreach (var r in scenario.Rows)
        {
            var category = r.Category is { } name && MatchCatalog.Default.Find(name) is { } known ? known.Name : "";
            _rows.Rows.Add(DayText(r.Date), r.Payee, r.Amount.ToString("0.00", Et), r.Description, r.Currency, r.MoneyIn, category, r.TypeCode ?? "", r.Declare switch { true => "1", false => "0", null => "" });
        }
    }

    /// <summary>Runs the scenario the fields say and shows what it gave.</summary>
    internal void RunNow()
    {
        if (Scenario() is not { } scenario)
        {
            return;
        }

        UseWaitCursor = true;
        try
        {
            Result = Simulator.Run(scenario, _schemaRoot, savedCatalog: _savedCatalog);
            ShowResult(Result);
        }
        catch (Exception e)
        {
            ClearResult();
            Tell(UiText.Worded(e) ?? e.Message);
        }
        finally
        {
            UseWaitCursor = false;
        }
    }

    /// <summary>Nothing of an earlier run stays on the screen as if it were the scenario's now shown — after a failed run,
    /// and after a scenario file opened (the fifth audit, item 26).</summary>
    private void ClearResult()
    {
        Result = null;
        _lines.Rows.Clear();
        _review.Text = "";
        _xml.Text = "";
    }

    private void ShowResult(SimResult result)
    {
        _lines.Rows.Clear();
        foreach (var line in result.Lines.OrderBy(l => l.LineNumber, StringComparer.Ordinal))
        {
            var r = line.Result;
            _lines.Rows.Add(
                line.LineNumber, UiText.ActionText(line.Action), line.Line.TypeCode, line.Line.DocumentDate is { } day ? DayText(day) : "",
                Money(r?.Net ?? line.NetPaid), Money(r?.Gross ?? line.Line.Amount), Money(r?.IncomeTax), Money(r?.ExemptionUsed), Money(r?.SocialTax),
                Money(r?.SocialTaxMinimumIncrease), Money(r?.UnemploymentEmployee), Money(r?.UnemploymentEmployer), Money(r?.FundedPension), Core.Report.ReportText.SourceText(line.SourceRef));
        }

        _review.Text = UiText.SimReport(result);
        _xml.Text = UiText.SimXmlText(result);
        _results.SelectedIndex = result.Refusal is null && result.Lines.Count > 0 ? 0 : 1;
    }

    private void SaveScenario()
    {
        if (Scenario() is not { } scenario)
        {
            return;
        }

        var path = ChoosingFile is { } choose ? choose("simulatsioon.json") : AskSavePath();
        if (path is null)
        {
            return;
        }

        try
        {
            File.WriteAllText(path, scenario.ToJson(), new UTF8Encoding(false));
        }
        catch (Exception e)
        {
            Tell(e.Message);
        }
    }

    private void OpenScenario()
    {
        var path = OpeningFile is { } open ? open() : AskOpenPath();
        if (path is null)
        {
            return;
        }

        try
        {
            Fill(SimScenario.Parse(File.ReadAllText(path)));
            ClearResult(); // a file refused leaves the fields and the results as they were
        }
        catch (ArgumentException)
        {
            Tell(UiText.SimNotAScenario);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException or NotSupportedException)
        {
            Tell(e.Message);
        }
    }

    private string? AskSavePath()
    {
        using var dialog = new SaveFileDialog { Filter = UiText.SimFilter, FileName = "simulatsioon.json", DefaultExt = "json" };
        return dialog.ShowDialog(this) == DialogResult.OK ? dialog.FileName : null;
    }

    private string? AskOpenPath()
    {
        using var dialog = new OpenFileDialog { Filter = UiText.SimFilter, CheckFileExists = true };
        return dialog.ShowDialog(this) == DialogResult.OK ? dialog.FileName : null;
    }

    private void Tell(string message)
    {
        if (Warning is { } take)
        {
            take(message);
            return;
        }

        MessageBox.Show(this, message, UiText.Problem, MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    private void AddTab(string title, Control content)
    {
        var page = new TabPage(title);
        page.Controls.Add(content);
        _results.TabPages.Add(page);
    }

    private static DateOnly? Day(object? value, string label, List<string> problems)
    {
        var text = (value as string)?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        if (DateOnly.TryParseExact(text, DayFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
        {
            return day;
        }

        problems.Add(UiText.SimNotADay(label, text));
        return null;
    }

    /// <summary>A figure as typed: a comma or a point for the decimals, spaces (no-break ones too) between the thousands.</summary>
    private static bool ParseNumber(string? text, out decimal value) =>
        decimal.TryParse(text?.Replace(" ", "", StringComparison.Ordinal).Replace("\u00a0", "", StringComparison.Ordinal).Replace(',', '.'), NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value);

    private static string DayText(DateOnly? day) => day?.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture) ?? "";

    private static string Money(decimal? amount) => amount is { } a ? a.ToString("#,##0.00", Et) : "";

    private static void Select(ComboBox box, string value) => box.SelectedIndex = Math.Max(0, Choice.IndexOf(box.Items.Cast<Choice>(), value));

    private static Label Caption(string text) => new() { Text = text, AutoSize = true, Padding = new Padding(8, 6, 0, 0) };

    private static ComboBox Combo(Choice[] choices, int width)
    {
        var box = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = width };
        box.Items.AddRange(choices);
        box.SelectedIndex = 0;
        return box;
    }

    private static TextBox Output() => new() { ReadOnly = true, Multiline = true, ScrollBars = ScrollBars.Both, WordWrap = false, Dock = DockStyle.Fill, Font = new Font(FontFamily.GenericMonospace, 9f) };

    private static DataGridView EditGrid() => new()
    {
        Dock = DockStyle.Fill,
        AllowUserToAddRows = true,
        AllowUserToDeleteRows = true,
        RowHeadersWidth = 28,
        BackgroundColor = SystemColors.Window,
        EditMode = DataGridViewEditMode.EditOnEnter,
    };

    private static DataGridView ReadGrid() => new()
    {
        Dock = DockStyle.Fill,
        ReadOnly = true,
        AllowUserToAddRows = false,
        AllowUserToDeleteRows = false,
        RowHeadersVisible = false,
        BackgroundColor = SystemColors.Window,
    };
}
