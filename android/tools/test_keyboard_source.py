# SPDX-License-Identifier: GPL-3.0-or-later
from pathlib import Path
import unittest
class Tests(unittest.TestCase):
    def test_candidate_scroll_resets_before_signature_shortcut(self):
        source = (Path(__file__).parents[1] / 'Meltype.Android/KeyboardService.cs').read_text(encoding='utf-8')
        output = source.split('private void Output(')[1].split('private void InputSymbol(')[0]
        self.assertIn('_candidateScroll', source)
        self.assertLess(output.index('_candidateScroll?.ScrollTo(0, 0)'), output.index('if (_candidateSignature == signature) return'))
        self.assertIn('previousView?.SelectedClause != view?.SelectedClause', output)
    def test_external_composition_reset_invalidates_queued_output(self):
        source = (Path(__file__).parents[1] / 'Meltype.Android/KeyboardService.cs').read_text(encoding='utf-8')
        update = source.split('public override void OnUpdateSelection(')[1].split('private void Input(char c)')[0]
        self.assertIn('EditorComposition.ShouldReset(_preeditActive, newSelStart, newSelEnd, candidatesStart, candidatesEnd)', update)
        self.assertLess(update.index('Interlocked.Increment(ref _generation)'), update.index('_worker?.Post(() => _session?.Reset())'))
        self.assertIn('_choosingBrackets = false', update)
        self.assertIn('_shownView = null', update)
    def test_english_mode_resets_when_hidden_and_editor_starts(self):
        source = (Path(__file__).parents[1] / 'Meltype.Android/KeyboardService.cs').read_text(encoding='utf-8')
        start = source.split('public override void OnStartInput(')[1].split('public override void OnFinishInput()')[0]
        hide = source.split('public override void OnFinishInputView(')[1].split('public override void OnUpdateSelection(')[0]
        self.assertIn('_english = false;', start)
        self.assertIn('_english = false;', hide)
        self.assertIn('_session?.SetEnglish(false)', hide)
    def test_arrow_passes_shift_before_consumption(self):
        source = (Path(__file__).parents[1] / 'Meltype.Android/KeyboardService.cs').read_text(encoding='utf-8')
        special = source.split('private void Special(int key)')[1].split('private void Queue(')[0]
        self.assertIn('var shift = _shiftState.Active', special)
        self.assertIn('_session?.Key(key, shift: shift)', special)

    def test_transient_hide_preserves_prediction_prefix(self):
        source = (Path(__file__).parents[1] / 'Meltype.Android/KeyboardService.cs').read_text(encoding='utf-8')
        output = source.split('private void Output(')[1].split('private void RenderCandidates(')[0]
        self.assertNotIn('if (operation is 1 or 2 or 7) _predictionPrefix = ""', output)
        queue = source.split('private void Queue(')[1].split('private void PostEditorUpdate(')[0]
        self.assertIn('if (string.IsNullOrEmpty(_shownView?.Text)) _predictionPrefix = ""', queue)

    def test_clipboard_offer_consumed_on_input_and_hide(self):
        source = (Path(__file__).parents[1] / 'Meltype.Android/KeyboardService.cs').read_text(encoding='utf-8')
        for method, end in [('private void Input(char c)', 'private void InputSpace('), ('public override void OnFinishInputView(', 'public override void OnUpdateSelection(')]:
            body = source.split(method)[1].split(end)[0]
            self.assertIn('_clipboardSuggestion.Dismiss()', body)
        self.assertIn('description.Timestamp', source)
        self.assertIn('PutLong("last-copy"', source)

    def test_release_has_no_typo_acquisition(self):
        root = Path(__file__).parents[1]
        for name in ['Meltype.Android/MainActivity.cs','Meltype.Android/KeyboardService.cs']:
            source = (root/name).read_text(encoding='utf-8')
            for removed in ['TRAINING_BUILD','TypoTrainingStore','TypoEditCapture','FinishTrainingQueued','Collecting()']:
                self.assertNotIn(removed, source)
        self.assertFalse((root/'Meltype.Android.Core/TypoTraining.cs').exists())

    def test_insets_not_double_added(self):
        source = (Path(__file__).parents[1] / 'Meltype.Android/KeyboardService.cs').read_text(encoding='utf-8')
        self.assertNotIn('padding + bottom', source)
        self.assertIn('Math.Max(padding, bottom)', source)
    def test_cursor_selection_before_key_fallback(self):
        source = (Path(__file__).parents[1] / 'Meltype.Android/KeyboardService.cs').read_text(encoding='utf-8')
        method = source.split('private void MoveCursor(Keycode code)')[1].split('private void Enter()')[0]
        self.assertLess(method.index('SetSelection'), method.index('SendKeyEvent'))
if __name__ == '__main__': unittest.main()
