# SPDX-License-Identifier: GPL-3.0-or-later
from pathlib import Path
import unittest


class Tests(unittest.TestCase):
    def service(self):
        return (Path(__file__).parents[1] / 'Meltype.Android/KeyboardService.cs').read_text(encoding='utf-8')

    def test_theme_reapplies_persistent_candidate_row_buttons(self):
        source = self.service()
        apply_theme = source.split('private bool ApplyTheme()')[1].split('public override void OnConfigurationChanged')[0]
        for button in ['_clipboardButton', '_oneHandButton', '_hide']:
            self.assertIn(f'MobileStyle.KeyboardKey({button}, function: true)', apply_theme,
                          f'{button} is created once and must be re-styled when the theme changes')

    def test_theme_change_compares_ime_applied_state(self):
        source = self.service()
        apply_theme = source.split('private bool ApplyTheme()')[1].split('public override void OnConfigurationChanged')[0]
        self.assertIn('var changed = _dark != MobileStyle.Dark;', apply_theme)
        self.assertIn('_dark = MobileStyle.Dark;', apply_theme)
        for method, end in [
            ('public override void OnConfigurationChanged', 'public override View OnCreateInputView()'),
            ('public override void OnStartInputView(', 'public override void OnFinishInput()'),
        ]:
            body = source.split(method)[1].split(end)[0]
            self.assertNotIn('var wasDark = MobileStyle.Dark;', body,
                             'MobileStyle.Dark is also written by MainActivity, so a shared delta misses changes')
            self.assertIn('if (ApplyTheme())', body)

    def test_night_ime_theme_overrides_navigation_bar(self):
        night = (Path(__file__).parents[1] / 'Meltype.Android/Resources/values-night/styles.xml').read_text(encoding='utf-8')
        ime = night.split('<style name="MeltypeImeTheme"')[1].split('</style>')[0]
        self.assertIn('<item name="android:navigationBarColor">#1B1D21</item>', ime)
        self.assertIn('<item name="android:windowLightNavigationBar">false</item>', ime)


if __name__ == '__main__':
    unittest.main()
