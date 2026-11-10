using System.Globalization;
using SatellesTSD.Core.Declare;
using SatellesTSD.Core.Model;
using SatellesTSD.Core.Tax;

namespace SatellesTSD;

/// <summary>The upload checklist shown once a file is written: where it is, and what to do in e-MTA.</summary>
public sealed class ChecklistDialog : Form
{
    public ChecklistDialog(ExportResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        Text = UiText.ChecklistTitle;
        Size = new Size(720, 420);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        var path = new TextBox { ReadOnly = true, Text = result.Path, Dock = DockStyle.Top, Margin = new Padding(8) };
        var summary = new Label { Dock = DockStyle.Top, AutoSize = false, Height = 28, Padding = new Padding(8, 6, 8, 0), Text = $"{result.Lines} rida, uniqueID {result.Submission.UniqueId}" };
        var steps = new TextBox { ReadOnly = true, Multiline = true, Dock = DockStyle.Fill, ScrollBars = ScrollBars.Vertical, Text = UiText.ChecklistSteps.Replace("\n", Environment.NewLine), BorderStyle = BorderStyle.None, BackColor = SystemColors.Control };
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, FlowDirection = FlowDirection.RightToLeft };
        var ok = new Button { Text = UiText.Ok, DialogResult = DialogResult.OK, AutoSize = true };
        var open = new Button { Text = UiText.OpenFolder, AutoSize = true };
        open.Click += (_, _) =>
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"/select,\"{result.Path}\"") { UseShellExecute = true });
            }
            catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
            {
                MessageBox.Show(this, e.Message, UiText.Problem, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        };
        buttons.Controls.AddRange([ok, open]);
        Controls.Add(steps);
        Controls.Add(summary);
        Controls.Add(path);
        Controls.Add(buttons);
        AcceptButton = ok;
    }
}

/// <summary>The reason a file changes a confirmed declaration (PARANDUSPOHJUS_1..4), the accountant's choice: one for
/// each section the file carries, of the reasons that fit that section (decision 154). A file of one section asks once.</summary>
public sealed class CorrectionReasonDialog : FieldDialog
{
    private readonly Dictionary<Section, ComboBox> _reasons = [];

    public CorrectionReasonDialog(IReadOnlyList<Section> sections) : base(UiText.CorrectionReasonTitle)
    {
        ArgumentNullException.ThrowIfNull(sections);
        Field("", new Label { Text = sections.Count > 1 ? UiText.CorrectionReasonPromptPerSection : UiText.CorrectionReasonPrompt, AutoSize = true, MaximumSize = new Size(320, 0) });
        foreach (var section in sections)
        {
            var box = Combo(PeriodSubmitter.ReasonsFor(section).Select(c => new Choice(c, UiText.ReasonText(c))));
            _reasons[section] = box;
            Field(sections.Count > 1 ? UiText.SectionShort(section) : UiText.CorrectionReasonTitle, box);
        }

        Buttons();
    }

    /// <summary>The reason picked for each section.</summary>
    public IReadOnlyDictionary<Section, string> Reasons => _reasons.ToDictionary(r => r.Key, r => ((Choice)r.Value.SelectedItem!).Value);

    internal IReadOnlyDictionary<Section, ComboBox> ReasonBoxes => _reasons;
}

/// <summary>The workbook's calculator on the new engine: one amount, net or gross, with the facts as toggles; the
/// month's minimum through the allocator with that one payment; nothing stored.</summary>
public sealed class CalculatorDialog : FieldDialog
{
    private const NumberStyles Plain = NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite;

    private readonly TaxRates _rates;
    private readonly TextBox _amount = new();
    private readonly ComboBox _direction = Combo([new Choice("net", UiText.AmountIsNet), new Choice("gross", UiText.AmountIsGross)]);
    private readonly ComboBox _residency = Combo([new Choice("R", UiText.Resident), new Choice("N", UiText.NonResident)]);
    private readonly ComboBox _type;
    private readonly CheckBox _exemptionApplied = new() { Text = UiText.ExemptionApplied, AutoSize = true, Checked = true };
    private readonly TextBox _exemptionMonthly = new();
    private readonly ComboBox _member = Combo([new Choice("1", UiText.Yes), new Choice("0", UiText.No)]);
    private readonly ComboBox _rate = Combo([new Choice("0.02", "2 %"), new Choice("0.04", "4 %"), new Choice("0.06", "6 %")]);
    private readonly CheckBox _onPension = new() { Text = UiText.OnPension, AutoSize = true };
    private readonly CheckBox _minimum = new() { Text = UiText.SocialTaxMinimum, AutoSize = true, Checked = true };
    private readonly TextBox _share = new() { Text = "1" };
    private readonly TextBox _output = new() { ReadOnly = true, Multiline = true, Height = 220, ScrollBars = ScrollBars.Vertical, Font = new Font(FontFamily.GenericMonospace, 9f) };

    public CalculatorDialog(TaxRates? rates = null) : base(UiText.CalculatorTitle)
    {
        _rates = rates ?? TaxRates.Year2026;
        _type = Combo(PaymentTypes.All.Select(t => new Choice(t.Code, $"{t.Code} {t.NameEt}")), "10");
        Field(UiText.Amount, _amount);
        Field("", _direction);
        Field(UiText.Residency, _residency);
        Field(UiText.PaymentType, _type);
        Field("", _exemptionApplied);
        Field(UiText.ExemptionMonthly, _exemptionMonthly);
        Field(UiText.PensionMemberLabel, _member);
        Field(UiText.PensionRate, _rate);
        Field("", _onPension);
        Field("", _minimum);
        Field(UiText.MinimumShare, _share);
        var compute = new Button { Text = UiText.Compute, AutoSize = true };
        compute.Click += (_, _) => Compute();
        Field("", compute);
        Field("", _output);
        Buttons();
        AcceptButton = compute; // Enter computes; Esc and the buttons close
        _residency.SelectedIndexChanged += (_, _) =>
        {
            // a type of the other residency cannot stand; a type that fits is left as chosen
            var residency = _residency.SelectedIndex == 1 ? Residency.NonResident : Residency.Resident;
            if (_type.SelectedItem is Choice current && PaymentTypes.TryGet(current.Value, out var type) && type!.Payee == residency)
            {
                return;
            }

            _type.SelectedIndex = Math.Max(0, Choice.IndexOf(_type.Items.Cast<Choice>(), residency == Residency.NonResident ? "120" : "10"));
        };
    }

    private void Compute()
    {
        if (!decimal.TryParse(_amount.Text, Plain, CultureInfo.CurrentCulture, out var amount) && !decimal.TryParse(_amount.Text, Plain, CultureInfo.InvariantCulture, out amount))
        {
            Complain("Summa ei ole arv.");
            return;
        }

        decimal? exemption = null;
        if (!string.IsNullOrWhiteSpace(_exemptionMonthly.Text))
        {
            if (!decimal.TryParse(_exemptionMonthly.Text, Plain, CultureInfo.CurrentCulture, out var e) && !decimal.TryParse(_exemptionMonthly.Text, Plain, CultureInfo.InvariantCulture, out e))
            {
                Complain("Maksuvaba summa ei ole arv.");
                return;
            }

            exemption = e;
        }

        if (!decimal.TryParse(_share.Text, Plain, CultureInfo.CurrentCulture, out var share) && !decimal.TryParse(_share.Text, Plain, CultureInfo.InvariantCulture, out share))
        {
            share = 1m;
        }

        var type = PaymentTypes.Get(((Choice)_type.SelectedItem!).Value);
        var facts = new PayeeTaxFacts(
            _residency.SelectedIndex == 1 ? Residency.NonResident : Residency.Resident,
            ResidenceCountry: _residency.SelectedIndex == 1 ? "XX" : null,
            FundedPensionMember: _member.SelectedIndex == 0,
            PensionRate: decimal.Parse(((Choice)_rate.SelectedItem!).Value, CultureInfo.InvariantCulture),
            OnOldAgeOrFlexiblePension: _onPension.Checked,
            BasicExemptionApplied: _exemptionApplied.Checked,
            BasicExemptionMonthly: exemption,
            SocialTaxMinimumApplies: _minimum.Checked,
            SocialTaxMinimumShare: share <= 0m || share > 1m ? 1m : share);
        try
        {
            var allocator = new MonthAllocator(new TaxEngine(_rates));
            var today = DateOnly.FromDateTime(DateTime.Today);
            var allocation = allocator.Allocate([new MonthPayment("1", type, amount, _direction.SelectedIndex == 0, today)], facts);
            var r = allocation.Payments[0].Result;
            _output.Text = string.Join(Environment.NewLine,
            [
                $"{UiText.Gross,-28}{UiText.Money(r.Gross),14}",
                $"{UiText.Net,-28}{UiText.Money(r.Net),14}",
                $"{UiText.IncomeTax,-28}{UiText.Money(r.IncomeTax),14}",
                $"{UiText.ExemptionUsed,-28}{UiText.Money(r.ExemptionUsed),14}",
                $"{UiText.FundedPension,-28}{UiText.Money(r.FundedPension),14}",
                $"{UiText.UnemploymentEmployee,-28}{UiText.Money(r.UnemploymentEmployee),14}",
                $"{UiText.UnemploymentEmployer,-28}{UiText.Money(r.UnemploymentEmployer),14}",
                $"{UiText.SocialTax,-28}{UiText.Money(r.SocialTax),14}",
                $"{UiText.MinimumIncrease,-28}{UiText.Money(r.SocialTaxMinimumIncrease),14}",
                $"{UiText.EmployerCost,-28}{UiText.Money(r.EmployerCost),14}",
            ]);
        }
        catch (Exception e) when (e is InvalidOperationException or ArgumentException)
        {
            _output.Text = e.Message;
        }
    }
}
