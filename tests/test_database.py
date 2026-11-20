from contextlib import closing
import hashlib
import json
from pathlib import Path
import sqlite3
import sys
import unittest
from uuid import uuid4

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / 'tools'))
from extract_database import (clean_reading, export_windows, extract_file, hiragana_reading, kana,
                              parse_database, plain_meaning, read_image, write_sqlite, writing)


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

    def test_windows_export_preserves_cards_when_source_word_numbers_change(self):
        root = ROOT / 'Data' / f'test-dictionary-{uuid4().hex}'
        root.mkdir()
        self.addCleanup(root.rmdir)
        legacy = root / 'dictionary.sqlite'
        source = root / 'yarxi.db'
        target = root / 'dictionary.windows.sqlite'
        for path in (legacy, source, target):
            self.addCleanup(path.unlink, missing_ok=True)
        write_sqlite(legacy, [self.by_id[entry_id].copy() for entry_id in ('kanji-76', 'word-100', 'word-60000')])
        with closing(sqlite3.connect(source)) as db:
            db.executescript('''
                CREATE TABLE Kanji (Nomer, Uncd, Str, Onyomi, Kunyomi, RusNick, Russian);
                CREATE TABLE Tango (Nomer, K1, K2, K3, K4, Kana, Reading, Russian, Yo);
                INSERT INTO Kanji VALUES (76, 19968, 1, '*ichi*', '0*hitotsu*', 'один', 'один');
                INSERT INTO Kanji VALUES (917, 21512, 6, '*go:*', '0*au*', 'согласие', 'согласие');
                INSERT INTO Kanji VALUES (1472, 22259, 7, '*zu*', '0*hakaru*', 'план', 'план');
                INSERT INTO Tango VALUES (100, 0, 0, 0, -1, '0^aisuti:', 'aisuti:', 'зеленый чай со льдом', '2');
                INSERT INTO Tango VALUES (355, 917, 1472, 0, 0, '', 'aizu*Q1', 'сигнал, знак^012345', '');
                INSERT INTO Tango VALUES (420, 0, 0, 0, -1, '0wanwan', 'wanwan', 'гав-гав', '');
                INSERT INTO Tango VALUES (421, 0, 0, 0, -1, '0wanwan', 'wanwan', 'собака', '');
            ''')
        source_hash = hashlib.sha256(source.read_bytes()).hexdigest()
        manifest = export_windows(source, legacy, root)
        self.assertEqual(manifest['source_sha256'], source_hash)
        self.assertEqual(hashlib.sha256(source.read_bytes()).hexdigest(), source_hash)
        with closing(sqlite3.connect(target)) as db:
            rows = {entry_id: (number, text, meaning) for entry_id, number, text, meaning in db.execute(
                'SELECT id, source_id, text, meaning FROM entries WHERE eligible=1')}
        self.assertEqual(rows['kanji-76'][1], '一')
        self.assertEqual(rows['word-100'], (355, '合図', 'сигнал, знак'))
        self.assertEqual(rows['word-yarxi-100'], (100, 'アイスティー', 'зелёный чай со льдом'))
        self.assertEqual(rows['word-60000'][0], 60000)


if __name__ == '__main__':
    unittest.main()
