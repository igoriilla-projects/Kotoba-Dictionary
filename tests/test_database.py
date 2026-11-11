import hashlib
import json
from pathlib import Path
import sqlite3
import sys
import unittest

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / 'tools'))
from extract_database import clean_reading, extract_file, hiragana_reading, kana, parse_database, plain_meaning, read_image, writing


class DictionaryTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.raw = extract_file(read_image(ROOT / 'YARXI.dmg'), 'yarxice.db')
        cls.entries = parse_database(cls.raw)
        cls.by_id = {entry['id']: entry for entry in cls.entries}
        cls.db = sqlite3.connect(ROOT / 'Data' / 'dictionary.sqlite')

    @classmethod
    def tearDownClass(cls):
        cls.db.close()

    def test_extracted_database_is_lossless(self):
        self.assertEqual(self.raw, (ROOT / 'Data' / 'yarxice.db').read_bytes())
        manifest = json.loads((ROOT / 'Data' / 'manifest.json').read_text())
        self.assertEqual(hashlib.sha256(self.raw).hexdigest(), manifest['database_sha256'])
        self.assertEqual(hashlib.sha256((ROOT / 'YARXI.dmg').read_bytes()).hexdigest(), manifest['source_sha256'])

    def test_complete_record_export(self):
        self.assertEqual(len(self.entries), 66355)
        self.assertEqual(sum(e['kind'] == 'kanji' for e in self.entries), 6355)
        self.assertEqual(sum(e['kind'] == 'word' for e in self.entries), 60000)
        self.assertEqual(self.db.execute('select count(*) from entries').fetchone()[0], 66355)
        self.assertEqual(self.db.execute('pragma integrity_check').fetchone()[0], 'ok')
        self.assertEqual(self.db.execute('pragma user_version').fetchone()[0], 2)

    def test_known_dictionary_entries(self):
        one = self.by_id['kanji-76']
        self.assertEqual((one['text'], one['meaning'], one['strokes']), ('一', 'один', 1))
        self.assertEqual(self.by_id['kanji-100']['text'], '右')
        self.assertEqual(self.by_id['word-100']['text'], '合図')
        self.assertEqual(self.by_id['word-100']['meaning'], 'сигнал, знак')
        self.assertEqual(self.by_id['word-4']['text'], 'アーカンソー州')
        self.assertEqual(self.by_id['word-10000']['text'], '渇する')
        self.assertEqual(self.by_id['word-9512']['text'], '被ける')

    def test_fallback_kanji_gloss_and_unresolved_reference(self):
        rare = self.by_id['kanji-2968']
        self.assertTrue(rare['eligible'])
        self.assertEqual((rare['text'], rare['meaning']), ('丕', 'большой'))
        reference = self.by_id['word-4000']
        self.assertFalse(reference['eligible'])
        self.assertIn('003988', reference['raw_meaning'])

    def test_study_cards_have_real_japanese_and_russian(self):
        for entry in self.entries:
            if not entry['eligible']:
                continue
            with self.subTest(id=entry['id']):
                self.assertRegex(entry['text'], '[\u3040-\u30ff\u3400-\u9fff々]')
                self.assertRegex(entry['meaning'], '[А-Яа-яЁё]')
                self.assertNotRegex(entry['meaning'], r'[\\^#\x00-\x1f]')
                self.assertNotRegex(entry['reading'], r'[_^#*\x00-\x1f]')
                self.assertNotRegex(entry['hiragana'], '[A-Za-z]')

    def test_hiragana_readings_preserve_pronunciation(self):
        self.assertEqual(self.by_id['kanji-76']['hiragana'], 'いち, いつ / ひとつ')
        self.assertEqual(self.by_id['word-100']['hiragana'], 'あいず')
        self.assertEqual(self.by_id['word-53797']['hiragana'], 'みちならぬこい')
        self.assertEqual(hiragana_reading('gakkō'), 'がっこー')
        self.assertEqual(hiragana_reading('ookii'), 'おおきい')
        self.assertEqual(hiragana_reading('itchi'), 'いっち')
        self.assertEqual(hiragana_reading('goOn'), 'ごおん')
        self.assertEqual(hiragana_reading("shin'ya"), 'しんや')

    def test_hiragana_search_preserves_voiced_marks(self):
        reading, search = self.db.execute("select hiragana,search_text from entries where id='word-100'").fetchone()
        self.assertEqual(reading, 'あいず')
        self.assertIn('あいず', search)
        self.assertNotIn('あいす', search)

    def test_reading_directives_and_search_aliases_are_hidden(self):
        self.assertEqual(self.by_id['kanji-34']['meaning'], 'дешёвый')
        self.assertEqual(self.by_id['kanji-37']['meaning'], 'тёмный')
        self.assertNotIn('^', self.by_id['kanji-34']['reading'])
        self.assertEqual(clean_reading('*u*;*yu:**\x010*migi*'), 'u, yū / migi')
        self.assertEqual(clean_reading('atara*attara*'), 'atara')

    def test_kana_and_writing_instructions(self):
        self.assertEqual(kana('gakkou'), 'がっこう')
        self.assertEqual(kana('shinjuku'), 'しんじゅく')
        self.assertEqual(kana('a:kanso:', True), 'アーカンソー')
        self.assertEqual(kana('qi'), '々')
        self.assertEqual(writing([1, 2], '1i2ru', {1: '追', 2: '入'}, ''), '追い入る')
        with self.assertRaises(ValueError):
            writing([1], '1(4)', {1: '色'}, '')
        with self.assertRaises(ValueError):
            kana('invalid!')

    def test_markup_removes_links_and_keeps_gloss(self):
        self.assertEqual(plain_meaning(r'#\кн.#\увы\,\к сожалению^4-3311^^'), 'кн. увы, к сожалению')

    def test_manifest_matches_study_pool(self):
        manifest = json.loads((ROOT / 'Data' / 'manifest.json').read_text())
        for kind, expected in manifest['study_counts'].items():
            actual = self.db.execute('select count(*) from entries where kind=? and eligible=1', (kind,)).fetchone()[0]
            self.assertEqual(actual, expected)


if __name__ == '__main__':
    unittest.main()
