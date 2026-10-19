# SPDX-License-Identifier: GPL-3.0-or-later
import unittest
from roman_typo_analysis import Romanizer, classify, extract_model

class Tests(unittest.TestCase):
    def test_readings(self):
        r=Romanizer()
        self.assertEqual(r('きょうは学校に行く')[0],r('今日はがっこうにいく')[0])
        self.assertEqual(r('カタカナ')[0],r('かたかな')[0])
        self.assertEqual(r('あった')[0],'atta')
        self.assertIn('kj',r('してkjれる')[0])

    def test_classification(self):
        self.assertEqual(classify('がっこう','学校','gakkou','gakkou')[0],'not_typo')
        self.assertEqual(classify('これは','これは続きの文章','koreha','korehatsudukinobunnshou')[0],'not_typo')
        self.assertEqual(classify('x','y','chinamoni','chinamini')[0],'likely_typo')
        self.assertEqual(classify('x','y','rennrakuha','rennrakugatorenai')[0],'ambiguous')
        self.assertEqual(classify('x','y','same','same',False)[0],'ambiguous')
        self.assertEqual(classify('キャンペー','キャンペ','kyannpe-','kyannpe')[0],'ambiguous')
        self.assertEqual(classify('Andoroi','Andoroid','andoroi','andoroid')[0],'likely_typo')

    def test_model_requires_reviewed_independent_support_and_no_negative_evidence(self):
        sequence = iter(range(20))
        def row(label): return dict(pair=str(next(sequence)),category='likely_typo',wrong='o',right='i',label=label)
        self.assertEqual(extract_model([row('typo'),row('pending'),row('typo')])['Rules'],[])
        self.assertEqual(len(extract_model([row('typo') for _ in range(3)])['Rules']),1)
        self.assertEqual(extract_model([row('typo') for _ in range(3)]+[row('not_typo')])['Rules'],[])
        duplicate = row('typo')
        self.assertEqual(extract_model([duplicate,duplicate,duplicate])['Rules'],[])

if __name__ == '__main__': unittest.main()
