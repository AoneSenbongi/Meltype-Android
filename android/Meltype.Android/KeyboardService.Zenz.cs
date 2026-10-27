// SPDX-License-Identifier: GPL-3.0-or-later
using Meltype.AndroidCore;
using Meltype.Composition;
namespace Meltype.Mobile;

public sealed partial class KeyboardService
{
    private CachedContextEvaluator? _zenzEvaluator;
    private bool _zenzPollScheduled;
    private IContextEvaluator? ContextEvaluatorForEditor(bool allowLearning)
    {
        _zenzEvaluator?.ClearCaches();
        if (_preferences?.Current.LocalZenz != true)
        {
            _zenzEvaluator?.Dispose(); _zenzEvaluator = null; return null;
        }
        if (!allowLearning) return null;
        if (_zenzEvaluator is null)
        {
            if (!LocalZenz.Available(this)) return null;
            var native = LocalZenz.Create(this);
            _zenzEvaluator = new CachedContextEvaluator(native);
            _ = native.WarmUpAsync();
        }
        return _zenzEvaluator;
    }
    private void ScheduleZenzPoll()
    {
        if (_zenzPollScheduled || _session?.ContextEvaluationPending != true || _worker is null) return;
        _zenzPollScheduled = true;
        var generation = Volatile.Read(ref _generation);
        _worker.PostDelayed(() =>
        {
            _zenzPollScheduled = false;
            if (generation != Volatile.Read(ref _generation)) { ScheduleZenzPoll(); return; }
            _jobGeneration = generation;
            _session?.PollContextEvaluation(Environment.TickCount64);
            ScheduleZenzPoll();
        }, 8);
    }
}
