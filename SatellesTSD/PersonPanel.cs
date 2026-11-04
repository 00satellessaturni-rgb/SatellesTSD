using System.Globalization;
using SatellesTSD.Core.Matching;
using SatellesTSD.Core.Model;
using SatellesTSD.Core.Store;
using SatellesTSD.Core.Tax;

namespace SatellesTSD;

/// <summary>The person's facts, as the accountant establishes them by hand: the names and codes, the residency and the
/// II pillar (each with where it came from and when it was checked; a change or a confirmation makes it the
/// accountant's word), the exemption, the minimum, A1, the treaty rate, the employment type, the notes. What is typed
/// and not yet saved is remembered (<see cref="IsDirty"/>) and never dropped in silence.</summary>
public sealed class PersonPanel : UserControl
{
    private const NumberStyles Plain = NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite;

    private static readonly Choice[] ResidencyChoices = [new("R", UiText.Resident), new("N", UiText.NonResident)];
    private static readonly Choice[] MemberChoices = [new("", UiText.Unknown), new("1", UiText.Yes), new("0", UiText.No)];
    private static readonly Choice[] RateChoices = [new("", UiText.Unknown), new("0.02", "2 %"), new("0.04", "4 %"), new("0.06", "6 %")];
    private static readonly Choice[] A1Choices = [new("0", UiText.A1None), new("1", UiText.A1Estonian), new("2", UiText.A1Foreign)];
    private static readonly Choice[] TorTypeChoices = [new("", "—"), .. EmploymentTypes.All.Select(t => new Choice(t.TorTypeId, $"{t.TorTypeId} {t.Name}"))];

    private readonly TableLayoutPanel _table = new() { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, Padding = new Padding(6) };
    private readonly Label _header = new() { AutoSize = true, Font = new Font(Control.DefaultFont, FontStyle.Bold) };
    private readonly TextBox _first = new();
    private readonly TextBox _last = new();
    private readonly TextBox _code = new();
    private readonly TextBox _mrr = new();
    private readonly ComboBox _residency = Combo(ResidencyChoices);
    private readonly TextBox _country = new() { CharacterCasing = CharacterCasing.Upper, MaxLength = 2 };
    private readonly Label _residencyInfo = new() { AutoSize = true, ForeColor = Color.DimGray };
    private readonly CheckBox _confirmResidency = new() { Text = UiText.ConfirmByHand, AutoSize = true };
    private readonly ComboBox _member = Combo(MemberChoices);
    private readonly ComboBox _rate = Combo(RateChoices);
    private readonly Label _pensionInfo = new() { AutoSize = true, ForeColor = Color.DimGray };
    private readonly CheckBox _confirmPension = new() { Text = UiText.ConfirmByHand, AutoSize = true };
    private readonly CheckBox _onPension = new() { Text = UiText.OnPension, AutoSize = true };
    private readonly TextBox _pensionAgeYear = new();
    private readonly CheckBox _exemptionApplied = new() { Text = UiText.ExemptionApplied, AutoSize = true };
    private readonly TextBox _exemptionMonthly = new();
    private readonly CheckBox _minimum = new() { Text = UiText.SocialTaxMinimum, AutoSize = true };
    private readonly ComboBox _a1 = Combo(A1Choices);
    private readonly TextBox _a1Country = new() { CharacterCasing = CharacterCasing.Upper, MaxLength = 2 };
    private readonly TextBox _workCountry = new() { CharacterCasing = CharacterCasing.Upper, MaxLength = 2 };
    private readonly TextBox _treatyRate = new();
    private readonly ComboBox _torType = Combo(TorTypeChoices);
    private readonly TextBox _notes = new() { Multiline = true, Height = 60, ScrollBars = ScrollBars.Vertical };
    private readonly FlowLayoutPanel _buttons = new() { AutoSize = true, FlowDirection = FlowDirection.LeftToRight };
    private readonly Button _save = new() { Text = UiText.Save, AutoSize = true };
    private readonly Button _revert = new() { Text = UiText.Revert, AutoSize = true };
    private readonly Button _lookUp = new() { Text = UiText.LookUp, AutoSize = true };
    private Person? _person;
    private bool _editable;
    private bool _filling;

    public event Action<Person, bool, bool>? SaveRequested;

    public event Action<Person>? LookUpRequested;

    public PersonPanel()
    {
        AutoScroll = true;
        _table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        Row(_header, span: true);
        Row(UiText.FirstName, _first);
        Row(UiText.LastName, _last);
        Row(UiText.PersonalCode, _code);
        Row(UiText.MrrCode, _mrr);
        Row(UiText.Residency, _residency);
        Row(UiText.ResidenceCountry, _country);
        Row(_residencyInfo, span: true);
        Row(_confirmResidency, span: true);
        Row(UiText.PensionMemberLabel, _member);
        Row(UiText.PensionRate, _rate);
        Row(_pensionInfo, span: true);
        Row(_confirmPension, span: true);
        Row(_onPension, span: true);
        Row(UiText.PensionAgeYear, _pensionAgeYear);
        Row(_exemptionApplied, span: true);
        Row(UiText.ExemptionMonthly, _exemptionMonthly);
        Row(new Label { Text = UiText.ExemptionFull, AutoSize = true, ForeColor = Color.DimGray }, span: true);
        Row(_minimum, span: true);
        Row(UiText.A1, _a1);
        Row(UiText.A1Country, _a1Country);
        Row(UiText.WorkCountry, _workCountry);
        Row(UiText.TreatyRate + ", %", _treatyRate);
        Row(UiText.TorType, _torType);
        Row(UiText.Notes, _notes);
        _buttons.Controls.AddRange([_save, _revert, _lookUp]);
        Row(_buttons, span: true);
        Controls.Add(_table);

        foreach (var box in new[] { _first, _last, _code, _mrr, _country, _pensionAgeYear, _exemptionMonthly, _a1Country, _workCountry, _treatyRate, _notes })
        {
            box.TextChanged += (_, _) => Touched();
        }

        foreach (var combo in new[] { _residency, _member, _rate, _a1, _torType })
        {
            combo.SelectedIndexChanged += (_, _) => Touched();
        }

        foreach (var check in new[] { _confirmResidency, _confirmPension, _onPension, _exemptionApplied, _minimum })
        {
            check.CheckedChanged += (_, _) => Touched();
        }

        _save.Click += (_, _) => Save();
        _revert.Click += (_, _) => Present(_person, _editable);
        _lookUp.Click += (_, _) => { if (_person is not null) { LookUpRequested?.Invoke(_person); } };
        Present(null, false);
    }

    /// <summary>Whether something was typed since the last Present or save.</summary>
    public bool IsDirty { get; private set; }

    /// <summary>The person shown, for a caller to tell a reload of the same person from a move to another.</summary>
    public Guid? PresentedId => _person?.Id;

    public bool ConfirmResidency => _confirmResidency.Checked;

    public bool ConfirmPension => _confirmPension.Checked;

    /// <summary>Whether the registers can be asked at all (the window knows).</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool LookupsAvailable { get; set; } = true;

    public void MarkClean() => IsDirty = false;

    /// <summary>Shows a person's facts (a fresh copy: the panel edits what it was shown, nothing older).</summary>
    public void Present(Person? person, bool editable)
    {
        _filling = true;
        _person = person;
        _editable = editable;
        var on = person is not null;
        _header.Text = person is null ? UiText.PersonPanel : $"{person.LastName} {person.FirstName}";
        _first.Text = person?.FirstName ?? "";
        _last.Text = person?.LastName ?? "";
        _code.Text = person?.PersonalCode ?? "";
        _mrr.Text = person?.MrrCode ?? "";
        _residency.SelectedIndex = person?.Residency == Residency.NonResident ? 1 : 0;
        _country.Text = person?.ResidenceCountry ?? "";
        _residencyInfo.Text = person is null ? "" : Checked(person.ResidencyCheckedOn, person.ResidencySource);
        _confirmResidency.Checked = false;
        _member.SelectedIndex = person?.FundedPensionMember switch { true => 1, false => 2, _ => 0 };
        FillRates(person?.PensionRate);
        _pensionInfo.Text = person is null ? "" : Checked(person.PensionCheckedOn, person.PensionSource);
        _confirmPension.Checked = false;
        _onPension.Checked = person?.OnOldAgeOrFlexiblePension == true;
        _pensionAgeYear.Text = person?.PensionAgeYear?.ToString(CultureInfo.InvariantCulture) ?? "";
        _exemptionApplied.Checked = person?.BasicExemptionApplied == true;
        // the allowance in force stands in the box unless the person has one of their own (a smaller one by their application)
        _exemptionMonthly.Text = person is null ? "" : (person.BasicExemptionMonthly ?? TaxRates.Year2026.BasicExemptionMonth).ToString("0.00", CultureInfo.CurrentCulture);
        _minimum.Checked = person?.SocialTaxMinimumApplies ?? true;
        _a1.SelectedIndex = (int)(person?.A1 ?? A1Case.None);
        _a1Country.Text = person?.A1Country ?? "";
        _workCountry.Text = person?.WorkCountry ?? "";
        _treatyRate.Text = person?.TreatyIncomeTaxRate is { } rate ? (rate * 100).ToString("0.####", CultureInfo.CurrentCulture) : "";
        _torType.SelectedIndex = Math.Max(0, Choice.IndexOf(TorTypeChoices, person?.TorEmploymentTypeId));
        _notes.Text = person?.Notes ?? "";
        foreach (Control control in _table.Controls)
        {
            if (control is not Label)
            {
                control.Enabled = on && (editable || control == _buttons);
            }
        }

        _save.Enabled = on && editable;
        _revert.Enabled = on && editable;
        _lookUp.Enabled = on && LookupsAvailable && person?.PersonalCode is { } code && Core.Import.PersonalCode.LooksValid(code);
        _filling = false;
        IsDirty = false;
    }

    /// <summary>The person as the fields say, or null after a complaint about a field that is not a number.</summary>
    public Person? Collect()
    {
        if (_person is null)
        {
            return null;
        }

        if (!TryDecimal(_exemptionMonthly.Text, out var exemption) || !TryDecimal(_treatyRate.Text, out var treaty) || !TryInt(_pensionAgeYear.Text, out var pensionAgeYear))
        {
            MessageBox.Show(this, UiText.NotANumber, UiText.Problem, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return null;
        }

        return _person with
        {
            FirstName = _first.Text,
            LastName = _last.Text,
            PersonalCode = Null(_code.Text),
            MrrCode = Null(_mrr.Text),
            Residency = _residency.SelectedIndex == 1 ? Residency.NonResident : Residency.Resident,
            ResidenceCountry = Null(_country.Text),
            FundedPensionMember = _member.SelectedIndex switch { 1 => true, 2 => false, _ => null },
            PensionRate = _rate.SelectedItem is Choice { Value.Length: > 0 } rate ? decimal.Parse(rate.Value, CultureInfo.InvariantCulture) : null,
            OnOldAgeOrFlexiblePension = _onPension.Checked,
            PensionAgeYear = pensionAgeYear,
            BasicExemptionApplied = _exemptionApplied.Checked,
            BasicExemptionMonthly = exemption == TaxRates.Year2026.BasicExemptionMonth ? null : exemption, // the allowance in force is the default, never a figure of the person's own
            SocialTaxMinimumApplies = _minimum.Checked,
            A1 = (A1Case)Math.Max(0, _a1.SelectedIndex),
            A1Country = Null(_a1Country.Text),
            WorkCountry = Null(_workCountry.Text),
            TreatyIncomeTaxRate = treaty is { } t ? t / 100m : null,
            TorEmploymentTypeId = _torType.SelectedItem is Choice { Value.Length: > 0 } type ? type.Value : null,
            Notes = Null(_notes.Text),
        };
    }

    private void Save()
    {
        if (Collect() is { } edited)
        {
            SaveRequested?.Invoke(edited, _confirmResidency.Checked, _confirmPension.Checked);
        }
    }

    private void Touched()
    {
        if (!_filling)
        {
            IsDirty = true;
        }
    }

    /// <summary>The three rates of the law, and the stored one when it is another (never lost on a save).</summary>
    private void FillRates(decimal? stored)
    {
        _rate.Items.Clear();
        _rate.Items.AddRange(RateChoices);
        var value = stored?.ToString("0.####", CultureInfo.InvariantCulture);
        var index = Choice.IndexOf(RateChoices, value);
        if (index < 0 && value is not null)
        {
            _rate.Items.Add(new Choice(value, UiText.Percent(stored)));
            index = _rate.Items.Count - 1;
        }

        _rate.SelectedIndex = Math.Max(0, index);
    }

    private static string Checked(DateOnly? on, Core.Lookups.LookupSource source) =>
        on is { } day ? $"{UiText.CheckedOn} {UiText.Day(day)} ({UiText.SourceOf}: {UiText.Source(source)})" : UiText.NotChecked;

    private static ComboBox Combo(Choice[] choices)
    {
        var box = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 220 };
        box.Items.AddRange(choices);
        return box;
    }

    private void Row(string label, Control control)
    {
        var row = _table.RowCount++;
        _table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _table.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 6, 3, 3) }, 0, row);
        control.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        _table.Controls.Add(control, 1, row);
    }

    private void Row(Control control, bool span)
    {
        var row = _table.RowCount++;
        _table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _table.Controls.Add(control, 0, row);
        if (span)
        {
            _table.SetColumnSpan(control, 2);
        }
    }

    private static string? Null(string text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    /// <summary>A number as the accountant types it: the Estonian comma, or the invariant dot; no thousands separators.</summary>
    private static bool TryDecimal(string text, out decimal? value)
    {
        value = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        if (decimal.TryParse(text, Plain, CultureInfo.CurrentCulture, out var current))
        {
            value = current;
            return true;
        }

        if (decimal.TryParse(text, Plain, CultureInfo.InvariantCulture, out var invariant))
        {
            value = invariant;
            return true;
        }

        return false;
    }

    private static bool TryInt(string text, out int? value)
    {
        value = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            value = parsed;
            return true;
        }

        return false;
    }
}
