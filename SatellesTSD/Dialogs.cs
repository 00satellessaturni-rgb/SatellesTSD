using System.Globalization;
using SatellesTSD.Core.Matching;
using SatellesTSD.Core.Model;
using SatellesTSD.Core.Review;
using SatellesTSD.Core.Store;
using SatellesTSD.Core.Tax;

namespace SatellesTSD;

/// <summary>A small dialog: a table of labelled fields and OK / Cancel.</summary>
public abstract class FieldDialog : Form
{
    private readonly TableLayoutPanel _table = new() { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2, Padding = new Padding(8) };

    protected FieldDialog(string title)
    {
        Text = title;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 320));
        Controls.Add(_table);
    }

    protected void Field(string label, Control control)
    {
        var row = _table.RowCount++;
        _table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _table.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 6, 3, 3) }, 0, row);
        control.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        _table.Controls.Add(control, 1, row);
    }

    protected void Buttons()
    {
        var ok = new Button { Text = UiText.Ok, DialogResult = DialogResult.OK, AutoSize = true };
        var cancel = new Button { Text = UiText.Cancel, DialogResult = DialogResult.Cancel, AutoSize = true };
        var panel = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill };
        panel.Controls.AddRange([cancel, ok]);
        var row = _table.RowCount++;
        _table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _table.Controls.Add(panel, 0, row);
        _table.SetColumnSpan(panel, 2);
        AcceptButton = ok;
        CancelButton = cancel;
        ok.Click += (_, e) => { if (!Accept()) { DialogResult = DialogResult.None; } };
    }

    /// <summary>Checks the fields when OK is pressed; false keeps the dialog open.</summary>
    protected virtual bool Accept() => true;

    protected static ComboBox Combo(IEnumerable<Choice> choices, string? selected = null)
    {
        var box = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        box.Items.AddRange(choices.ToArray<object>());
        box.SelectedIndex = Math.Max(0, Choice.IndexOf(box.Items.Cast<Choice>(), selected));
        return box;
    }

    protected static string? Null(string text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    protected void Complain(string message) => MessageBox.Show(this, message, UiText.Problem, MessageBoxButtons.OK, MessageBoxIcon.Warning);
}

/// <summary>A new client company: registry code and name.</summary>
public sealed class CompanyDialog : FieldDialog
{
    private readonly TextBox _code = new();
    private readonly TextBox _name = new();

    public CompanyDialog() : base(UiText.NewCompanyTitle)
    {
        Field(UiText.Name, _name);
        Field(UiText.RegistryCode, _code);
        Buttons();
    }

    public string RegistryCode => _code.Text.Trim();

    public string CompanyTitle => _name.Text.Trim();

    protected override bool Accept()
    {
        if (RegistryCode.Length == 0 || CompanyTitle.Length == 0)
        {
            Complain("Registrikood ja nimi on vajalikud.");
            return false;
        }

        return true;
    }
}

/// <summary>A person added by hand: names, the Estonian personal code, the residency and the employment type.</summary>
public sealed class PersonDialog : FieldDialog
{
    private readonly TextBox _first = new();
    private readonly TextBox _last = new();
    private readonly TextBox _code = new();
    private readonly ComboBox _residency = Combo([new Choice("R", UiText.Resident), new Choice("N", UiText.NonResident)]);
    private readonly ComboBox _type = Combo([new Choice("", "—"), .. EmploymentTypes.All.Select(t => new Choice(t.TorTypeId, $"{t.TorTypeId} {t.Name}"))]);

    public PersonDialog() : base(UiText.NewPersonTitle)
    {
        Field(UiText.FirstName, _first);
        Field(UiText.LastName, _last);
        Field(UiText.PersonalCode, _code);
        Field(UiText.Residency, _residency);
        Field(UiText.TorType, _type);
        Buttons();
    }

    public string FirstName => _first.Text.Trim();

    public string LastName => _last.Text.Trim();

    public string? PersonalCode => Null(_code.Text);

    public Residency Residency => _residency.SelectedIndex == 1 ? Residency.NonResident : Residency.Resident;

    public string? TorTypeId => _type.SelectedItem is Choice { Value.Length: > 0 } c ? c.Value : null;

    protected override bool Accept()
    {
        if (FirstName.Length == 0 || LastName.Length == 0)
        {
            Complain("Ees- ja perekonnanimi on vajalikud.");
            return false;
        }

        return true;
    }
}

/// <summary>A payment added by hand: who, when, how much, to whom, and how it is to be declared.</summary>
public sealed class HandLineDialog : FieldDialog
{
    private readonly ComboBox _person;
    private readonly DateTimePicker _date;
    private readonly TextBox _amount = new();
    private readonly TextBox _payee = new();
    private readonly TextBox _description = new();
    private readonly ComboBox _category;
    private readonly ComboBox _type;
    private readonly CheckBox _declare = new() { Text = UiText.Declare, ThreeState = true, CheckState = CheckState.Indeterminate, AutoSize = true };
    private readonly TextBox _note = new();
    private decimal _parsedAmount;

    public HandLineDialog(ReviewView view, Guid? personId) : base(UiText.AddLineTitle)
    {
        ArgumentNullException.ThrowIfNull(view);
        var persons = new List<Choice> { new("", UiText.NoPerson) };
        persons.AddRange(view.StorePersons.Select(p => new Choice(p.Id.ToString("N"), $"{p.LastName} {p.FirstName} {p.PersonalCode ?? p.MrrCode ?? ""}".Trim())));
        _person = Combo(persons, personId?.ToString("N"));
        var first = new DateTime(view.Period.Year, view.Period.Month, 1);
        _date = new DateTimePicker { Format = DateTimePickerFormat.Custom, CustomFormat = "dd.MM.yyyy", Value = first.AddMonths(1).AddDays(-1) };
        _category = Combo([new Choice("", "—"), .. view.Catalog.Categories.Select(c => new Choice(c.Name, c.Name))]);
        _type = Combo([new Choice("", UiText.Default), .. PaymentTypes.All.Select(t => new Choice(t.Code, $"{t.Code} {t.NameEt}"))]);
        if (_person.SelectedItem is Choice { Value.Length: > 0 } && view.StorePersons.Count > 0)
        {
            _category.SelectedIndex = Math.Max(0, Choice.IndexOf(_category.Items.Cast<Choice>(), view.Catalog.Categories.FirstOrDefault(c => c.IsSalary)?.Name));
            if (view.StorePersons.FirstOrDefault(p => p.Id == personId) is { } person)
            {
                _payee.Text = $"{person.FirstName} {person.LastName}";
            }
        }

        Field(UiText.Person, _person);
        Field(UiText.Date, _date);
        Field(UiText.Amount, _amount);
        Field(UiText.Payee, _payee);
        Field(UiText.Description, _description);
        Field(UiText.Category, _category);
        Field(UiText.PaymentType, _type);
        Field("", _declare);
        Field(UiText.Note, _note);
        Buttons();
    }

    public Guid? PersonId => _person.SelectedItem is Choice { Value.Length: > 0 } c ? Guid.ParseExact(c.Value, "N") : null;

    public DateOnly Date => DateOnly.FromDateTime(_date.Value);

    public decimal Amount => _parsedAmount;

    public string Payee => _payee.Text.Trim();

    public string? Description => Null(_description.Text);

    public string? Category => _category.SelectedItem is Choice { Value.Length: > 0 } c ? c.Value : null;

    public string? TypeCode => _type.SelectedItem is Choice { Value.Length: > 0 } c ? c.Value : null;

    public bool? Declare => _declare.CheckState switch { CheckState.Checked => true, CheckState.Unchecked => false, _ => null };

    public string? Note => Null(_note.Text);

    protected override bool Accept()
    {
        var text = _amount.Text.Trim();
        const NumberStyles plain = NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint;
        if (!decimal.TryParse(text, plain, CultureInfo.CurrentCulture, out _parsedAmount) && !decimal.TryParse(text, plain, CultureInfo.InvariantCulture, out _parsedAmount))
        {
            Complain("Summa ei ole arv.");
            return false;
        }

        if (_parsedAmount < 0m || _parsedAmount != Math.Round(_parsedAmount, 2))
        {
            Complain("Summa peab olema sentides, mitte alla nulli.");
            return false;
        }

        if (Payee.Length == 0)
        {
            Complain("Saaja on vajalik.");
            return false;
        }

        return true;
    }
}

/// <summary>The files the period's run reads, and the way to take one away (the run goes with it, the decisions on
/// that file's rows too).</summary>
public sealed class FilesDialog : Form
{
    private readonly AppServices _services;
    private readonly Action<string> _log;
    private readonly ListView _list = new() { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, MultiSelect = false };
    private readonly Button _unlink = new() { Text = UiText.Unlink, AutoSize = true, Enabled = false };
    private readonly Button _reread = new() { Text = UiText.Reread, AutoSize = true, Enabled = false };
    private readonly Button _remove = new() { Text = UiText.RemoveFile, AutoSize = true, Enabled = false };
    private readonly Guid _periodId;
    private readonly Guid _companyId;
    private readonly bool _editable;

    public FilesDialog(AppServices services, ReviewView view, Action<string> log)
    {
        ArgumentNullException.ThrowIfNull(view);
        _services = services ?? throw new ArgumentNullException(nameof(services));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _periodId = view.Period.Id;
        _companyId = view.Company.Id;
        _editable = view.Editable;
        Text = UiText.FilesTitle;
        Size = new Size(700, 360);
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        _list.Columns.Add(UiText.Name, 320);
        _list.Columns.Add(UiText.Kind, 90);
        _list.Columns.Add(UiText.ImportedAt, 140);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft };
        var close = new Button { Text = UiText.Ok, DialogResult = DialogResult.OK, AutoSize = true };
        buttons.Controls.AddRange([close, _remove, _reread, _unlink]);
        Controls.Add(_list);
        Controls.Add(buttons);
        AcceptButton = close;
        _list.SelectedIndexChanged += (_, _) => EnableButtons();
        _unlink.Click += (_, _) => Unlink();
        _reread.Click += (_, _) => Reread();
        _remove.Click += (_, _) => Remove();
        Fill();
    }

    // the window tests' seams
    internal Button UnlinkButton => _unlink;
    internal Button RereadButton => _reread;
    internal Button RemoveButton => _remove;
    internal ListView List => _list;

    private ImportFile? Selected => _list.SelectedItems.Count == 0 ? null : _list.SelectedItems[0].Tag as ImportFile;

    /// <summary>The file read again with the layouts in force (decision 131): what it left is logged, a refusal shown.</summary>
    private void Reread()
    {
        if (Selected is not { } file || MessageBox.Show(this, UiText.RereadConfirm, UiText.Reread, MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
        {
            return;
        }

        try
        {
            var outcome = _services.Pipeline.RereadFile(_periodId, file.Id);
            foreach (var message in outcome.Messages)
            {
                _log($"{UiText.Reread}: {file.FileName}: {message}");
            }

            if (outcome.Succeeded)
            {
                _log(UiText.RereadDone(file.FileName, outcome.Carried, outcome.Dropped, outcome.DraftsDeleted, outcome.RunsCleared));
            }
            else
            {
                _log($"{UiText.Reread}: {file.FileName}: {outcome.Error}");
                MessageBox.Show(this, outcome.Error, UiText.Reread, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
        catch (Exception e)
        {
            MessageBox.Show(this, e.Message, UiText.Problem, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        Fill();
    }

    /// <summary>The file taken out of the store (decision 132): out of the period's files first, with the run's second word as
    /// for an unlink, then out of the store with its own refusals.</summary>
    private void Remove()
    {
        if (Selected is not { } file || MessageBox.Show(this, UiText.RemoveFileConfirm, UiText.RemoveFile, MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
        {
            return;
        }

        try
        {
            try
            {
                _services.Pipeline.RemoveFile(_periodId, file.Id);
            }
            catch (InvalidOperationException e) when (e.Message.Contains("match run", StringComparison.Ordinal))
            {
                if (MessageBox.Show(this, UiText.UnlinkClearsRun, UiText.RemoveFile, MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
                {
                    return;
                }

                _services.Pipeline.RemoveFile(_periodId, file.Id, clearRun: true); // the run goes in the same transaction: a refusal costs no search
            }

            _log($"{UiText.RemoveFile}: {file.FileName}");
        }
        catch (Exception e)
        {
            MessageBox.Show(this, e.Message, UiText.Problem, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        Fill();
    }

    private void Fill()
    {
        _list.Items.Clear();
        var linked = _services.Store.PeriodImports(_periodId);
        foreach (var file in linked)
        {
            _list.Items.Add(new ListViewItem([file.FileName, file.Kind.ToString(), file.ImportedAt.ToLocalTime().ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture)]) { Tag = file });
        }

        // the company's files no period lists any more, after the period's own: the one place they can be taken out of the store
        foreach (var file in _services.Pipeline.UnlistedFiles(_companyId))
        {
            _list.Items.Add(new ListViewItem([file.FileName, $"{file.Kind} ({UiText.Unlisted})", file.ImportedAt.ToLocalTime().ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture)]) { Tag = file, ForeColor = SystemColors.GrayText });
        }

        EnableButtons();
    }

    private void EnableButtons()
    {
        var listed = Selected is { } file && _services.Store.PeriodImports(_periodId).Any(f => f.Id == file.Id);
        _unlink.Enabled = _reread.Enabled = _editable && listed;
        _remove.Enabled = _editable && Selected is not null;
    }

    private void Unlink()
    {
        if (Selected is not { } file)
        {
            return;
        }

        if (MessageBox.Show(this, UiText.UnlinkConfirm, UiText.Unlink, MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
        {
            return;
        }

        try
        {
            try
            {
                _services.Pipeline.UnlinkFile(_periodId, file.Id);
            }
            catch (InvalidOperationException e) when (e.Message.Contains("match run", StringComparison.Ordinal))
            {
                // the run took the file: it goes only with the accountant's second word
                if (MessageBox.Show(this, UiText.UnlinkClearsRun, UiText.Unlink, MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK)
                {
                    return;
                }

                _services.Pipeline.ClearRun(_periodId);
                _services.Pipeline.UnlinkFile(_periodId, file.Id);
            }

            _log($"{UiText.Unlink}: {file.FileName}");
        }
        catch (Exception e)
        {
            MessageBox.Show(this, e.Message, UiText.Problem, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        Fill();
    }
}
