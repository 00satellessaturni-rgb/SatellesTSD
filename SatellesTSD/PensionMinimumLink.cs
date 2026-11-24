namespace SatellesTSD;

/// <summary>"Vanaduspensionil" ticked by the accountant's hand takes the social tax minimum off: a state pension's
/// recipient owes none (decision 237, the mirror of decision 173). Unticked again, the minimum comes back when the tick
/// took it off; a hand touch of the minimum ends that, the accountant's own word from there. Nothing happens while the
/// owner fills the boxes (a stored card with both ticks is shown as stored; the build notes it).
/// „Osaline või puuduv töövõime“ does not touch the minimum's tick (decision 266): the engine owes no minimum while the
/// status stands, whatever the tick says, and a status ends with its term — a tick taken off for it and saved would stay
/// off after the status, since what took it off is known only while the card is open.</summary>
internal sealed class PensionMinimumLink
{
    private readonly CheckBox _pension;
    private readonly CheckBox _minimum;
    private readonly Func<bool> _filling;
    private bool _takenOff;
    private bool _setting;

    public PensionMinimumLink(CheckBox pension, CheckBox minimum, Func<bool> filling, ToolTip tips)
    {
        _pension = pension;
        _minimum = minimum;
        _filling = filling;
        tips.SetToolTip(minimum, UiText.MinimumPensionTip);
        pension.CheckedChanged += (_, _) => ReasonChanged();

        minimum.CheckedChanged += (_, _) =>
        {
            if (!_setting)
            {
                _takenOff = false; // touched by hand, or filled: not the tick's doing any more
            }
        };
    }

    /// <summary>Whether the minimum's tick was taken off by a reason's tick (and would come back with its untick).</summary>
    public bool TakenOff => _takenOff;

    /// <summary>A card shown afresh: nothing on it was taken off by the tick.</summary>
    public void Reset() => _takenOff = false;

    private void ReasonChanged()
    {
        if (_filling())
        {
            return;
        }

        if (_pension.Checked && _minimum.Checked)
        {
            Set(false);
            _takenOff = true;
        }
        else if (!_pension.Checked && _takenOff && !_minimum.Checked)
        {
            Set(true);
            _takenOff = false;
        }
    }

    private void Set(bool value)
    {
        _setting = true;
        try
        {
            _minimum.Checked = value;
        }
        finally
        {
            _setting = false;
        }
    }
}

/// <summary>The work-ability status and the application under it (SMS § 6 lg 1 p 5): Töötukassa pays the social tax only
/// for a person with the status, so the application's tick is open only while the status is ticked, and the status's
/// untick takes the application off with it — also while a card is filled, so a stored application without the status
/// (which every payment would refuse, WORK_ABILITY_APPLICATION_WITHOUT_STATUS) is never shown as standing.</summary>
internal sealed class WorkAbilityLink
{
    private readonly CheckBox _status;
    private readonly CheckBox _application;
    private readonly Func<bool> _editable;

    /// <param name="editable">Whether the window lets the accountant edit at all (a card shown read-only keeps both shut).</param>
    public WorkAbilityLink(CheckBox status, CheckBox application, Func<bool> editable)
    {
        _status = status;
        _application = application;
        _editable = editable;
        status.CheckedChanged += (_, _) => Refresh();
        Refresh();
    }

    /// <summary>Puts the application's tick in step with the status's (the window calls it after its own enabling).</summary>
    public void Refresh()
    {
        if (!_status.Checked && _application.Checked)
        {
            _application.Checked = false;
        }

        _application.Enabled = _editable() && _status.Checked;
    }
}
