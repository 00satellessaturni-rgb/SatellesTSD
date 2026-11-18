using SatellesTSD.Core.Model;

namespace SatellesTSD;

/// <summary>The A1 case decides one of its countries (decision 222, KR_YLD2_07/09): Estonia's A1 is EE, a foreign A1's
/// work is in Estonia. The card and the simulation's card share it; each keeps its own guard for the time it is being
/// filled in, since nothing is filled then.</summary>
internal static class A1Fill
{
    /// <summary>The picked case's fixed country put in as EE — only into an empty box, never over text typed in.</summary>
    public static void FixedCountry(A1Case picked, TextBox a1Country, TextBox workCountry)
    {
        if (picked == A1Case.EstonianA1WorkAbroad && a1Country.Text.Length == 0)
        {
            a1Country.Text = "EE";
        }
        else if (picked == A1Case.ForeignA1WorkInEstonia && workCountry.Text.Length == 0)
        {
            workCountry.Text = "EE";
        }
    }

    /// <summary>The hint row that says the pairing.</summary>
    public static Label Hint() => new() { Text = UiText.A1CountriesHint, AutoSize = true, ForeColor = Color.DimGray };
}
