using System.Globalization;
using SatellesTSD.Core.Matching;
using SatellesTSD.Core.Model;
using SatellesTSD.Core.Store;
using SatellesTSD.Core.Tax;

namespace SatellesTSD;

/// <summary>The person's facts, as the accountant establishes them by hand: the names and codes, the residency and the
/// II pillar (each with where it came from and when it was checked; a change or a confirmation makes it the
/// accountant's word), the exemption, the minimum, the work ability and its application, A1, the treaty rate, the employment type, the notes. What is typed
/// and not yet saved is remembered (<see cref="IsDirty"/>) and never dropped in silence.</summary>
public sealed class PersonPanel : UserControl
{
    private const NumberStyles Plain = NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite;

    private static readonly Choice[] ResidencyChoices = [new("R", UiText.Resident), new("N", UiText.NonResident)];
    private static readonly Choice[] MemberChoices = [new("", UiText.Unknown), new("1", UiText.Yes), new("0", UiText.No)];
    private static readonly Choice[] RateChoices = [new("", UiText.Unknown), new("0.02", "2 %"), new("0.04", "4 %"), new("0.06", "6 %")];
    private static readonly Choice[] A1Choices = [new("0", UiText.A1None), new("1", UiText.A1Estonian), new("2", UiText.A1Foreign)];
    private static readonly Choice[] DeclarationChoices = [new("Auto", UiText.DeclarationAuto), new("Include", UiText.DeclarationInclude), new("Exclude", UiText.DeclarationExclude)];
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
    private readonly CheckBox _workAbility = new() { Text = UiText.ReducedWorkAbility, AutoSize = true };
    private readonly CheckBox _statePays = new() { Text = UiText.StatePaysSocialTax, AutoSize = true, Margin = new Padding(20, 3, 3, 3) };
    private readonly TextBox _applicationDate = new() { MaxLength = 10 };
    private readonly Label _workAbilityNote = new() { Text = UiText.StatePaysSocialTaxNote, AutoSize = true, ForeColor = Color.DimGray, MaximumSize = new Size(380, 0), Margin = new Padding(20, 0, 3, 3) };
    private readonly ComboBox _declaration = Combo(DeclarationChoices);
    private readonly ComboBox _a1 = Combo(A1Choices);
    private readonly TextBox _a1Country = new() { CharacterCasing = CharacterCasing.Upper, MaxLength = 2 };
    private readonly TextBox _workCountry = new() { CharacterCasing = CharacterCasing.Upper, MaxLength = 2 };
    private readonly TextBox _treatyRate = new();
    private readonly ComboBox _torType = Combo(TorTypeChoices);
    private readonly CheckBox _typeFromCard = new() { Text = UiText.TypeFromCard, AutoSize = true };
    private readonly TextBox _notes = new() { Multiline = true, Height = 60, ScrollBars = ScrollBars.Vertical };
    private readonly FlowLayoutPanel _buttons = new() { AutoSize = true, FlowDirection = FlowDirection.LeftToRight };
    private readonly Button _save = new() { Text = UiText.Save, AutoSize = true };
    private readonly Button _revert = new() { Text = UiText.Revert, AutoSize = true };
    private readonly Button _lookUp = new() { Text = UiText.LookUp, AutoSize = true };
    private Person? _person;
    private bool _editable;
    private int _year = DateTime.Today.Year;
    private TaxRates _rates = TaxRates.Year2026;
    private decimal? _shownExemption; // what the exemption box was filled with, so an untouched box keeps the stored figure
    private bool _filling;
    private readonly ToolTip _tips = new();
    private readonly PensionMinimumLink _pensionMinimum;
    private readonly WorkAbilityLink _workAbilityLink;

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
        Row(_workAbility, span: true);
        Row(_statePays, span: true);
        Row(UiText.WorkAbilityApplicationDate, _applicationDate);
        _tips.SetToolTip(_applicationDate, UiText.WorkAbilityApplicationDateTip);
        // what the app cannot check, said where the application is ticked (KR_SM_03): not a tip alone. Shown only while the
        // status is ticked (the owner, 2026-11-24): a card of anyone else has no use for it
        Row(_workAbilityNote, span: true);
        _workAbility.CheckedChanged += (_, _) => _workAbilityNote.Visible = _workAbility.Checked;
        _workAbilityNote.Visible = _workAbility.Checked;
        Row(UiText.DeclarationLabel, _declaration);
        Row(UiText.A1, _a1);
        Row(UiText.A1Country, _a1Country);
        Row(UiText.WorkCountry, _workCountry);
        Row(A1Fill.Hint(), span: true);
        Row(UiText.TreatyRate + ", %", _treatyRate);
        Row(UiText.TorType, _torType);
        Row(_typeFromCard, span: true);
        Row(UiText.Notes, _notes);
        _buttons.Controls.AddRange([_save, _revert, _lookUp]);
        Row(_buttons, span: true);
        Controls.Add(_table);

        // the application's day goes with the application's tick: shut without it
        _statePays.CheckedChanged += (_, _) => _applicationDate.Enabled = _statePays.Enabled && _statePays.Checked;
        _statePays.EnabledChanged += (_, _) => _applicationDate.Enabled = _statePays.Enabled && _statePays.Checked;
        foreach (var box in new[] { _first, _last, _code, _mrr, _country, _pensionAgeYear, _exemptionMonthly, _applicationDate, _a1Country, _workCountry, _treatyRate, _notes })
        {
            box.TextChanged += (_, _) => Touched();
        }

        foreach (var combo in new[] { _residency, _member, _rate, _a1, _torType, _declaration })
        {
            combo.SelectedIndexChanged += (_, _) => Touched();
        }

        foreach (var check in new[] { _confirmResidency, _confirmPension, _onPension, _exemptionApplied, _minimum, _workAbility, _statePays, _typeFromCard })
        {
            check.CheckedChanged += (_, _) => Touched();
        }

        // set to a non-resident by the accountant's hand — picked, or confirmed as it stands: the basic exemption's
        // application goes off with it, there to be seen and ticked again before the save if this person is to have it
        // (decision 173)
        _residency.SelectedIndexChanged += (_, _) => ExemptionOffForNonResident();
        _confirmResidency.CheckedChanged += (_, _) => { if (_confirmResidency.Checked) { ExemptionOffForNonResident(); } };
        _exemptionApplied.CheckedChanged += (_, _) =>
        {
            // ticked again by hand after a non-resident pick took it off (an EEA resident keeps it, decision 144): the
            // person's own figure comes back with it, else the save would drop it for the full allowance (decision 186)
            if (_exemptionTakenOff && _exemptionApplied.Checked && _exemptionMonthly.Text.Length == 0)
            {
                _exemptionMonthly.Text = _exemptionTextTakenOff;
            }

            _exemptionTakenOff = false; // the box touched: the accountant's own word from here
        };
        _a1.SelectedIndexChanged += (_, _) =>
        {
            // the A1 case decides one of its countries, only an empty box filled (the fifth audit, items 14 and 21);
            // never while the card is filled from the store
            if (!_filling)
            {
                A1Fill.FixedCountry((A1Case)Math.Max(0, _a1.SelectedIndex), _a1Country, _workCountry);
            }
        };
        // "Vanaduspensionil" ticked by hand takes the minimum off, unticked gives it back (decision 237)
        // and so does the work-ability status (SMS § 2 lg 4 p 6), by the same link
        _pensionMinimum = new PensionMinimumLink(_onPension, _minimum, () => _filling, _tips);
        // the application only with the status: shut without it, and taken off with its untick
        _workAbilityLink = new WorkAbilityLink(_workAbility, _statePays, () => _person is not null && _editable);
        Disposed += (_, _) => _tips.Dispose();
        _save.Click += (_, _) => Save();
        _revert.Click += (_, _) => Present(_person, _editable);
        _lookUp.Click += (_, _) => { if (_person is not null) { LookUpRequested?.Invoke(_person); } };
        Present(null, false);
    }

    /// <summary>Whether something was typed since the last Present or save.</summary>
    public bool IsDirty { get; private set; }

    /// <summary>The person shown, for a caller to tell a reload of the same person from a move to another.</summary>
    public Guid? PresentedId => _person?.Id;

    /// <summary>The copy the card was filled from: what the accountant's edits are measured against when saved, so that a
    /// fact changed in the store meanwhile (the registers' answer) is not written over by the card's stale copy.</summary>
    public Person? Presented => _person;

    /// <summary>Seams for the window tests: the declaration choice and the save button.</summary>
    internal ComboBox DeclarationBox => _declaration;

    internal ComboBox TorTypeBox => _torType;

    internal CheckBox TypeFromCardBox => _typeFromCard;

    internal Button SaveButton => _save;

    internal Button LookUpButton => _lookUp;

    /// <summary>Takes a complaint about the card's fields in place of the message box (the window tests set it).</summary>
    internal Action<string>? Complaining;

    internal TextBox ExemptionBox => _exemptionMonthly;

    internal TextBox FirstNameBox => _first;

    internal TextBox MrrBox => _mrr;

    internal ComboBox A1Box => _a1;

    internal TextBox A1CountryBox => _a1Country;

    internal TextBox WorkCountryBox => _workCountry;

    internal CheckBox OnPensionBox => _onPension;

    internal CheckBox MinimumBox => _minimum;

    internal CheckBox WorkAbilityBox => _workAbility;

    internal CheckBox StatePaysBox => _statePays;

    internal ToolTip Tips => _tips;

    public bool ConfirmResidency => _confirmResidency.Checked;

    public bool ConfirmPension => _confirmPension.Checked;

    /// <summary>Whether the registers can be asked at all (the window knows).</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool LookupsAvailable { get; set; } = true;

    public void MarkClean() => IsDirty = false;

    /// <summary>Shows a person's facts for a month of <paramref name="year"/>, whose rates say the exemption in force.</summary>
    public void Present(Person? person, bool editable, int year, TaxRates rates)
    {
        _year = year;
        _rates = rates ?? throw new ArgumentNullException(nameof(rates));
        Present(person, editable);
    }

    /// <summary>The tick was taken off by the residency picked, not by the accountant: picking "resident" again puts it back.</summary>
    private bool _exemptionTakenOff;
    private string _exemptionTextTakenOff = "";

    private void ExemptionOffForNonResident()
    {
        if (_filling)
        {
            return;
        }

        if (_residency.SelectedIndex == 1 && _exemptionApplied.Checked)
        {
            _exemptionApplied.Checked = false;
            // the person's own figure goes with the application (a figure without one refuses every payment, decision 186);
            // kept here so that resident picked again gives it back
            _exemptionTextTakenOff = _exemptionMonthly.Text;
            _exemptionMonthly.Text = "";
            _exemptionTakenOff = true; // after the box's own handler, which reads a change of the box as the accountant's
        }
        else if (_residency.SelectedIndex == 0 && _exemptionTakenOff && !_exemptionApplied.Checked)
        {
            // non-resident picked by mistake (or passed with the wheel or the arrow keys) and resident picked again: a
            // resident does not lose the application over it
            _exemptionApplied.Checked = true;
            if (_exemptionMonthly.Text.Length == 0)
            {
                _exemptionMonthly.Text = _exemptionTextTakenOff;
            }

            _exemptionTakenOff = false;
        }
    }

    internal TextBox ApplicationDateBox => _applicationDate;

    internal ComboBox ResidencyBox => _residency;

    internal CheckBox ConfirmResidencyBox => _confirmResidency;

    internal CheckBox ExemptionAppliedBox => _exemptionApplied;

    /// <summary>Shows a person's facts (a fresh copy: the panel edits what it was shown, nothing older), for the year last given.</summary>
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
        // the allowance in force for the month's year stands in the box (776 from the pension-age year), unless the person
        // has one of their own (a smaller one by their application)
        _shownExemption = person?.ExemptionMonthInForce(_year, _rates);
        _exemptionMonthly.Text = _shownExemption is { } shown ? shown.ToString("0.00", CultureInfo.CurrentCulture) : "";
        _minimum.Checked = person?.SocialTaxMinimumApplies ?? true;
        _workAbility.Checked = person?.ReducedWorkAbility == true; // before the application: its untick takes the application off
        _statePays.Checked = person is { ReducedWorkAbility: true, StatePaysSocialTax: true };
        _applicationDate.Text = person is { ReducedWorkAbility: true, StatePaysSocialTax: true, WorkAbilityApplicationDate: { } applied } ? applied.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture) : "";
        _declaration.SelectedIndex = (int)(person?.Declaration ?? DeclarationChoice.Auto);
        _a1.SelectedIndex = (int)(person?.A1 ?? A1Case.None);
        _a1Country.Text = person?.A1Country ?? "";
        _workCountry.Text = person?.WorkCountry ?? "";
        _treatyRate.Text = person?.TreatyIncomeTaxRate is { } rate ? (rate * 100).ToString("0.####", CultureInfo.CurrentCulture) : "";
        FillTorTypes(person?.TorEmploymentTypeId);
        _typeFromCard.Checked = person?.TypeFromCard ?? false;
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
        _lookUp.Enabled = on && editable && LookupsAvailable && person?.PersonalCode is { } code && Core.Import.PersonalCode.LooksValid(code);
        _workAbilityLink.Refresh(); // after the loop above, which opened every box of an editable card
        _applicationDate.Enabled = _statePays.Enabled && _statePays.Checked;
        _filling = false;
        _exemptionTakenOff = false; // a card shown afresh: nothing on it was taken off by a pick
        _pensionMinimum.Reset();
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
            if (Complaining is { } take)
            {
                take(UiText.NotANumber);
            }
            else
            {
                MessageBox.Show(this, UiText.NotANumber, UiText.Problem, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }

            return null;
        }

        // the application's day, only with the application: a day typed and the tick taken off since is not kept
        DateOnly? applicationDate = null;
        if (_workAbility.Checked && _statePays.Checked && !string.IsNullOrWhiteSpace(_applicationDate.Text))
        {
            if (!TryDate(_applicationDate.Text, out var applied))
            {
                if (Complaining is { } take)
                {
                    take(UiText.WorkAbilityDateInvalid);
                }
                else
                {
                    MessageBox.Show(this, UiText.WorkAbilityDateInvalid, UiText.Problem, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }

                return null;
            }

            applicationDate = applied;
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
            // the person's own figure goes with the application: unticked, nothing of it is stored (a figure without one
            // refuses every payment, EXEMPTION_WITHOUT_APPLICATION); the box keeps its text, so a re-tick before the save
            // gives it back (the fifth audit, item 20)
            BasicExemptionMonthly = _exemptionApplied.Checked ? ExemptionToStore(exemption, pensionAgeYear) : null,
            SocialTaxMinimumApplies = _minimum.Checked,
            ReducedWorkAbility = _workAbility.Checked,
            StatePaysSocialTax = _workAbility.Checked && _statePays.Checked,
            WorkAbilityApplicationDate = applicationDate,
            Declaration = (DeclarationChoice)Math.Max(0, _declaration.SelectedIndex),
            A1 = (A1Case)Math.Max(0, _a1.SelectedIndex),
            A1Country = Null(_a1Country.Text),
            WorkCountry = Null(_workCountry.Text),
            TreatyIncomeTaxRate = treaty is { } t ? t / 100m : null,
            TorEmploymentTypeId = _torType.SelectedItem is Choice { Value.Length: > 0 } type ? type.Value : null,
            TypeFromCard = _typeFromCard.Checked,
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

    /// <summary>The TÖR types the card offers, and a stored one the list does not know (any type of the register outside
    /// the app's own): shown and selected, so an untouched combo gives it back on a save (decision 188).</summary>
    private void FillTorTypes(string? stored)
    {
        _torType.Items.Clear();
        _torType.Items.AddRange(TorTypeChoices);
        var index = Choice.IndexOf(TorTypeChoices, stored);
        if (index < 0 && !string.IsNullOrEmpty(stored))
        {
            _torType.Items.Add(new Choice(stored, stored));
            index = _torType.Items.Count - 1;
        }

        _torType.SelectedIndex = Math.Max(0, index);
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
    /// <summary>The person's own figure to store for the exemption box: unchanged from what was shown keeps what was
    /// stored (a figure of the person's own stays theirs in every year); else nothing when the typed figure is the allowance
    /// in force for the month's year by the pension-age year as typed, else the personal code's (700, or 776 from that
    /// year), else the figure typed.</summary>
    private decimal? ExemptionToStore(decimal? typed, int? pensionAgeYear)
    {
        if (typed == _shownExemption)
        {
            return _person?.BasicExemptionMonthly;
        }

        // the age as the store reads it: the year typed, else the personal code's (decision 208)
        var inForce = Person.PensionAgeYearOf(pensionAgeYear, Null(_code.Text)) is { } y && _year >= y ? _rates.PensionAgeExemptionMonth : _rates.BasicExemptionMonth;
        return typed == inForce ? null : typed;
    }

    /// <summary>A day as the accountant types it: dd.MM.yyyy (a day or a month of one digit too), or yyyy-MM-dd.</summary>
    internal static bool TryDate(string text, out DateOnly value) =>
        DateOnly.TryParseExact(text.Trim(), ["d.M.yyyy", "dd.MM.yyyy", "yyyy-MM-dd"], CultureInfo.InvariantCulture, DateTimeStyles.None, out value)
        && value.Year is >= 2000 and <= 2100; // a slip of the year (0026) is no day: it would stand before every month

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
