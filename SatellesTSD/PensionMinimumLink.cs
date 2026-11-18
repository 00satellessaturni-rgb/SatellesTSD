namespace SatellesTSD;

/// <summary>"Vanaduspensionil" ticked by the accountant's hand takes the social tax minimum off: a state pension's
/// recipient owes none (decision 237, the mirror of decision 173). Unticked again, the minimum comes back when the
/// tick took it off; a hand touch of the minimum ends that, the accountant's own word from there. Nothing happens while
/// the owner fills the boxes (a stored card with both ticks is shown as stored; the build notes it).</summary>
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
        pension.CheckedChanged += (_, _) => PensionChanged();
        minimum.CheckedChanged += (_, _) =>
        {
            if (!_setting)
            {
                _takenOff = false; // touched by hand, or filled: not the tick's doing any more
            }
        };
    }

    /// <summary>Whether the minimum's tick was taken off by the pension tick (and would come back with its untick).</summary>
    public bool TakenOff => _takenOff;

    /// <summary>A card shown afresh: nothing on it was taken off by the tick.</summary>
    public void Reset() => _takenOff = false;

    private void PensionChanged()
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
