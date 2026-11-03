namespace SatellesTSD.Core.Model;

/// <summary>Data sections of the APA payroll taxonomy that a private company (äriühing) uses.
/// EE0201001 = töötasud (employment pay incl. VÕS, board members, employer sick pay);
/// EE0201002 = hüvitised, toetused jm (rent, interest, other income). EE0201003 (ESD) is not ours.</summary>
public enum Section
{
    EE0201001,
    EE0201002,
}

/// <summary>TOIMINGUTYYP2023ap: R = uus toiming, M = olemasoleva muutmine (resend the whole payment under the same key),
/// T = olemasoleva tühistamine (key + counter + T only; the key is dead afterwards).</summary>
public enum LineAction
{
    R,
    M,
    T,
}

public enum Residency
{
    Resident,
    NonResident,
}

/// <summary>A1 / E101 certificate situation. The former separate codes (11/19/22/122/125 for an Estonian A1 with work
/// abroad; 12/20/23/121/124 for a foreign A1 with work in Estonia) are now the base code plus two country entries.</summary>
public enum A1Case
{
    None,
    /// <summary>Estonian A1, work abroad: social tax, unemployment insurance and funded pension stay in Estonia; no income tax.</summary>
    EstonianA1WorkAbroad,
    /// <summary>Foreign A1, work in Estonia: only income tax (with the basic exemption); no social tax, no insurance, no pension.</summary>
    ForeignA1WorkInEstonia,
}

/// <summary>Which A1 variants a payment type has in MTA's matrix (columns J/K): a type reacts only to the A1 kinds it
/// has rows for; KR_YLD2_02/03 refuse the others.</summary>
[Flags]
public enum A1Kinds
{
    None = 0,
    Estonian = 1,
    Foreign = 2,
    Both = Estonian | Foreign,
}

/// <summary>Which basic-exemption measurable a payment carries.</summary>
public enum ExemptionKind
{
    None,
    /// <summary>MVT_610 — üldine maksuvaba tulu.</summary>
    Mvt610,
    /// <summary>MVT_650 — maksuvaba tulu vanaduspensioniealisele, from 1 January of the year the person reaches pension age.</summary>
    Mvt650,
}
