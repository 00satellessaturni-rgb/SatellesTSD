using System.Globalization;
using SatellesTSD.Core.Model;
using SatellesTSD.Core.Tax;

namespace SatellesTSD;

/// <summary>The rules matrix (decision 203): every payment type against the taxes it carries for a case of the person
/// set by the toggles above it, drawn from the type table and the engine's own rule (<see cref="RulesMatrix"/>), with a
/// search; below it the rates and amounts in force and the conditions the toggles do not show. It stores nothing.</summary>
public sealed class RulesDialog : Form
{
    private static readonly CultureInfo Et = CultureInfo.GetCultureInfo("et-EE");

    private readonly TaxRates _rates;
    private readonly ComboBox _residency = Combo([new Choice("R", UiText.Resident), new Choice("N", UiText.NonResident)]);
    private readonly CheckBox _member = new() { Text = UiText.RulesPensionMember, AutoSize = true, Checked = true };
    private readonly CheckBox _onPension = new() { Text = UiText.OnPension, AutoSize = true };
    private readonly CheckBox _pensionAge = new() { Text = UiText.RulesPensionAge, AutoSize = true };
    private readonly CheckBox _exemption = new() { Text = UiText.ExemptionApplied, AutoSize = true, Checked = true };
    private readonly CheckBox _minimum = new() { Text = UiText.SocialTaxMinimum, AutoSize = true, Checked = true };
    private readonly ComboBox _a1 = Combo([new Choice(nameof(A1Case.None), UiText.RulesA1None), new Choice(nameof(A1Case.EstonianA1WorkAbroad), UiText.RulesA1Estonian), new Choice(nameof(A1Case.ForeignA1WorkInEstonia), UiText.RulesA1Foreign)]);
    private readonly TextBox _treaty = new() { Width = 50 };
    private readonly TextBox _search = new() { Width = 200 };
    private readonly CheckBox _onlyCase = new() { Text = UiText.RulesOnlyCase, AutoSize = true };
    private readonly Label _shown = new() { AutoSize = true, Padding = new Padding(0, 6, 0, 0) };
    private readonly DataGridView _grid = Grid();
    private readonly DataGridView _ratesGrid = Grid();
    private readonly TextBox _notes = new() { ReadOnly = true, Multiline = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill };

    public RulesDialog(TaxRates? rates, int year)
    {
        _rates = rates ?? TaxRates.Year2026;
        Text = UiText.RulesTitle(rates is null ? 2026 : year);
        Size = new Size(1280, 760);
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        MinimizeBox = false;

        var toggles = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true, Padding = new Padding(4) };
        toggles.Controls.AddRange([_residency, _member, _onPension, _pensionAge, _exemption, _minimum, _a1,
            new Label { Text = UiText.RulesTreaty, AutoSize = true, Padding = new Padding(0, 6, 0, 0) }, _treaty]);
        var searchRow = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(4) };
        searchRow.Controls.AddRange([new Label { Text = UiText.RulesSearch, AutoSize = true, Padding = new Padding(0, 6, 0, 0) }, _search, _onlyCase, _shown]);
        if (rates is null)
        {
            searchRow.Controls.Add(new Label { Text = UiText.RulesYearMissing, AutoSize = true, Padding = new Padding(12, 6, 0, 0), ForeColor = Color.DarkRed });
        }

        foreach (var (name, header, width) in new[]
        {
            ("code", UiText.RulesCode, 48), ("name", UiText.RulesName, 300), ("section", UiText.RulesSection, 40), ("payee", UiText.RulesPayee, 90),
            ("tor", UiText.RulesTor, 150), ("tm", UiText.RulesIncomeTax, 70), ("mvt", UiText.RulesExemption, 72), ("sm", UiText.RulesSocialTax, 80),
            ("min", UiText.RulesMinimum, 78), ("tk", UiText.RulesUnemploymentEmployee, 80), ("ttk", UiText.RulesUnemploymentEmployer, 80),
            ("kp", UiText.RulesFundedPension, 70), ("zero", UiText.RulesZero, 70), ("treaty", UiText.RulesTreatyAllowed, 78), ("mvt640", UiText.RulesThirdPillar, 66),
        })
        {
            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = name, HeaderText = header, Width = width, SortMode = DataGridViewColumnSortMode.NotSortable });
        }

        _grid.Columns["name"]!.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
        foreach (var (name, header, width) in new[] { ("name", UiText.RulesRateName, 300), ("value", UiText.RulesRateValue, 520), ("field", UiText.RulesRateField, 150) })
        {
            _ratesGrid.Columns.Add(new DataGridViewTextBoxColumn { Name = name, HeaderText = header, Width = width, SortMode = DataGridViewColumnSortMode.NotSortable });
        }

        foreach (var (name, value, field) in UiText.RatesInForce(_rates))
        {
            _ratesGrid.Rows.Add(name, value, field);
        }

        _notes.Text = string.Join(Environment.NewLine, UiText.RulesNotes().Select(n => "• " + n));

        var below = new SplitContainer { Name = "below", Dock = DockStyle.Fill, Orientation = Orientation.Vertical, SplitterDistance = 640 };
        below.Panel1.Controls.Add(_ratesGrid);
        below.Panel1.Controls.Add(new Label { Text = UiText.RulesRatesTitle, Dock = DockStyle.Top, AutoSize = true, Font = new Font(Font, FontStyle.Bold), Padding = new Padding(2) });
        below.Panel2.Controls.Add(_notes);
        below.Panel2.Controls.Add(new Label { Text = UiText.RulesNotesTitle, Dock = DockStyle.Top, AutoSize = true, Font = new Font(Font, FontStyle.Bold), Padding = new Padding(2) });
        var split = new SplitContainer { Name = "matrix", Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterDistance = 440 };
        split.Panel1.Controls.Add(_grid);
        split.Panel2.Controls.Add(below);
        var close = new Button { Text = UiText.Ok, DialogResult = DialogResult.OK, AutoSize = true };
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft };
        buttons.Controls.Add(close);
        Controls.Add(split);
        Controls.Add(searchRow);
        Controls.Add(toggles);
        Controls.Add(buttons);
        AcceptButton = close;
        CancelButton = close;

        foreach (var box in new[] { _member, _onPension, _pensionAge, _exemption, _minimum, _onlyCase })
        {
            box.CheckedChanged += (_, _) => Fill();
        }

        _residency.SelectedIndexChanged += (_, _) => Fill();
        _a1.SelectedIndexChanged += (_, _) => Fill();
        _treaty.TextChanged += (_, _) => Fill();
        _search.TextChanged += (_, _) => Fill();
        Fill();
    }

    internal DataGridView MatrixGrid => _grid;
    internal DataGridView RatesGrid => _ratesGrid;
    internal string NotesText => _notes.Text;
    internal string ShownText => _shown.Text;
    internal ComboBox ResidencyBox => _residency;
    internal CheckBox PensionMemberBox => _member;
    internal CheckBox OnPensionBox => _onPension;
    internal CheckBox PensionAgeBox => _pensionAge;
    internal CheckBox ExemptionBox => _exemption;
    internal CheckBox MinimumBox => _minimum;
    internal ComboBox A1Box => _a1;
    internal TextBox TreatyBox => _treaty;
    internal TextBox SearchBox => _search;
    internal CheckBox OnlyCaseBox => _onlyCase;

    /// <summary>The case the toggles set; a treaty rate that is no percent of 0–100 is left out.</summary>
    internal RuleCase Case() => new(
        ((Choice)_residency.SelectedItem!).Value == "N" ? Residency.NonResident : Residency.Resident,
        _member.Checked,
        _onPension.Checked,
        _pensionAge.Checked,
        _exemption.Checked,
        _minimum.Checked,
        Enum.Parse<A1Case>(((Choice)_a1.SelectedItem!).Value),
        decimal.TryParse(_treaty.Text.Trim().Replace(',', '.'), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var percent) && percent is >= 0m and <= 100m ? percent / 100m : null);

    private void Fill()
    {
        var rule = Case();
        var rows = RulesMatrix.Rows(rule);
        _grid.Rows.Clear();
        var shown = 0;
        foreach (var row in rows.Where(r => r.Matches(_search.Text) && (!_onlyCase.Checked || r.PayeeMatches)))
        {
            var type = row.Type;
            var flags = row.Flags;
            var index = _grid.Rows.Add(
                type.Code,
                type.NameEt,
                row.SectionNumber,
                type.Payee == Residency.NonResident ? UiText.NonResident : UiText.Resident,
                string.Join(", ", row.Employments.Select(e => $"{e.TorTypeId} {e.Name}")),
                row.TreatyRefused ? UiText.RulesTreatyRefused : flags.IncomeTaxRate is { } rate ? Percent(rate) : UiText.RulesNone,
                flags.ExemptionKind switch { ExemptionKind.Mvt610 => "MVT_610", ExemptionKind.Mvt650 => "MVT_650", _ => UiText.RulesNone },
                flags.SocialTax ? Percent(_rates.SocialTax) : UiText.RulesNone,
                flags.SocialTaxMinimum ? UiText.Yes : UiText.RulesNone,
                flags.UnemploymentEmployee ? Percent(_rates.UnemploymentEmployee) : UiText.RulesNone,
                flags.UnemploymentEmployer ? Percent(_rates.UnemploymentEmployer) : UiText.RulesNone,
                flags.FundedPension ? "2/4/6 %" : UiText.RulesNone,
                type.ZeroAllowed ? UiText.Yes : UiText.RulesNone,
                type.TreatyRateAllowed ? UiText.RulesAllowed : UiText.RulesNone,
                type.ThirdPillarExemptionAllowed ? UiText.RulesAllowed : UiText.RulesNone);
            var gridRow = _grid.Rows[index];
            gridRow.Tag = row;
            if (row.TreatyRefused)
            {
                gridRow.Cells["tm"].ToolTipText = UiText.RulesTreatyRefusedTip;
            }

            if (!row.PayeeMatches)
            {
                gridRow.DefaultCellStyle.ForeColor = SystemColors.GrayText;
                foreach (DataGridViewCell cell in gridRow.Cells)
                {
                    cell.ToolTipText = UiText.RulesOtherPayee;
                }
            }

            shown++;
        }

        _shown.Text = UiText.RulesShown(shown, rows.Count);
    }

    private static string Percent(decimal fraction) => (fraction * 100m).ToString("0.##", Et) + " %";

    private static ComboBox Combo(Choice[] choices)
    {
        var box = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 190 };
        box.Items.AddRange(choices);
        box.SelectedIndex = 0;
        return box;
    }

    private static DataGridView Grid() => new()
    {
        Dock = DockStyle.Fill,
        ReadOnly = true,
        AllowUserToAddRows = false,
        AllowUserToDeleteRows = false,
        AllowUserToResizeRows = false,
        RowHeadersVisible = false,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        BackgroundColor = SystemColors.Window,
        ShowCellToolTips = true,
    };
}
