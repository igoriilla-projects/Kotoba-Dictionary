#!/usr/bin/env python3
"""Export a YARXI disk image or the SQLite dictionary used by newer YARXI releases.

Only Python's standard library is required. The UDIF/HFS+ reader deliberately
fails on unsupported compression or overflow extents instead of truncating data.
"""
from __future__ import annotations

import argparse
from collections import Counter, defaultdict
from contextlib import closing
import gzip
import hashlib
from io import BytesIO
from itertools import count
import json
from pathlib import Path
import plistlib
import re
import sqlite3
import struct
import tempfile
import unicodedata
from xml.etree import ElementTree
import zlib


def u16(data: bytes, offset: int) -> int:
    return struct.unpack_from('<H', data, offset)[0]


def u32(data: bytes, offset: int) -> int:
    return struct.unpack_from('<I', data, offset)[0]


def read_image(path: Path) -> bytes:
    """Return the HFS+ partition from a UDIF image."""
    data = path.read_bytes()
    trailer = data[-512:]
    if trailer[:4] != b'koly':
        raise ValueError('Not a UDIF disk image')
    offset, length = struct.unpack_from('>QQ', trailer, 216)
    resources = plistlib.loads(data[offset:offset + length])['resource-fork']['blkx']
    blocks = next(b['Data'] for b in resources if 'Apple_HFS' in b['Name'])
    _, sectors, base = struct.unpack_from('>QQQ', blocks, 8)
    partition = bytearray(sectors * 512)
    count = struct.unpack_from('>I', blocks, 200)[0]
    for i in range(count):
        kind, _, sector, size, offset, length = struct.unpack_from('>IIQQQQ', blocks, 204 + i * 40)
        packed = data[base + offset:base + offset + length]
        if kind == 0x80000005:
            raw = zlib.decompress(packed)
        elif kind == 1:
            raw = packed
        elif kind in (0, 2, 0x7ffffffe, 0xffffffff):
            continue
        else:
            raise ValueError(f'Unsupported UDIF chunk: {kind:#x}')
        if len(raw) != size * 512:
            raise ValueError('UDIF chunk length mismatch')
        partition[sector * 512:(sector + size) * 512] = raw
    if partition[1024:1026] != b'H+':
        raise ValueError('Expected an HFS+ partition')
    return bytes(partition)


def extract_file(partition: bytes, filename: str) -> bytes:
    block_size = struct.unpack_from('>I', partition, 1064)[0]

    def fork(record: bytes, offset: int) -> bytes:
        length = struct.unpack_from('>Q', record, offset)[0]
        extents = struct.iter_unpack('>II', record[offset + 16:offset + 80])
        data = b''.join(partition[s * block_size:(s + n) * block_size] for s, n in extents)
        if len(data) < length:
            raise ValueError('Overflow extents are not supported')
        return data[:length]

    catalog = fork(partition, 1024 + 272)
    node_size = struct.unpack_from('>H', catalog, 32)[0]
    matches = []
    for offset in range(0, len(catalog), node_size):
        node = catalog[offset:offset + node_size]
        if node[8] != 255:  # Leaf nodes only.
            continue
        count = struct.unpack_from('>H', node, 10)[0]
        for i in range(count):
            start = struct.unpack_from('>H', node, node_size - 2 * (i + 1))[0]
            key_size = struct.unpack_from('>H', node, start)[0]
            name_size = struct.unpack_from('>H', node, start + 6)[0]
            name = node[start + 8:start + 8 + name_size * 2].decode('utf-16be')
            record = start + 2 + key_size
            if name == filename and struct.unpack_from('>H', node, record)[0] == 2:
                matches.append(fork(node, record + 88))
    if len(matches) != 1:
        raise ValueError(f'Expected one {filename}, found {len(matches)}')
    return matches[0]


# YARXI uses Hepburn-like ASCII, ':' for long vowels, and qi/qe for
# the ideographic iteration mark and small katakana ke in writing instructions.
KANA = dict(zip(
    'a i u e o ka ki ku ke ko sa shi su se so ta chi tsu te to na ni nu ne no '
    'ha hi fu he ho ma mi mu me mo ya yu yo ra ri ru re ro wa wo n '
    'ga gi gu ge go za ji zu ze zo da di du de do ba bi bu be bo pa pi pu pe po'.split(),
    'あいうえおかきくけこさしすせそたちつてとなにぬねのはひふへほまみむめもやゆよらりるれろわをん'
    'がぎぐげござじずぜぞだぢづでどばびぶべぼぱぴぷぺぽ'))
for consonant, stem in {'k': 'き', 'g': 'ぎ', 'sh': 'し', 'j': 'じ', 'ch': 'ち',
                         'n': 'に', 'h': 'ひ', 'b': 'び', 'p': 'ぴ', 'm': 'み', 'r': 'り'}.items():
    for vowel, small in [('a', 'ゃ'), ('u', 'ゅ'), ('o', 'ょ')]:
        KANA[consonant + ('' if consonant in ('sh', 'j', 'ch') else 'y') + vowel] = stem + small
KANA.update({'she': 'しぇ', 'je': 'じぇ', 'che': 'ちぇ', 'fa': 'ふぁ', 'fi': 'ふぃ',
             'fe': 'ふぇ', 'fo': 'ふぉ', 'ti': 'てぃ', 'tu': 'とぅ', 'wi': 'うぃ',
             'we': 'うぇ', 'va': 'ゔぁ', 'vi': 'ゔぃ', 'vu': 'ゔ', 've': 'ゔぇ',
             'vo': 'ゔぉ', 'qi': '々', 'qe': 'ヶ'})


def kana(text: str, katakana: bool = False) -> str:
    result = ''
    while text:
        if text[0] == ':':
            result += 'ー'
            text = text[1:]
            continue
        if text[0] in " '-":
            text = text[1:]
            continue
        if len(text) > 1 and text[0] == text[1] and text[0] not in 'aeioun':
            result += 'っ'
            text = text[1:]
            continue
        if text.startswith('n') and (len(text) == 1 or text[1] not in 'aiueoy'):
            result += 'ん'
            text = text[1:]
            continue
        for size in (3, 2, 1):
            if text[:size] in KANA:
                result += KANA[text[:size]]
                text = text[size:]
                break
        else:
            raise ValueError(f'Unsupported romanization: {text!r}')
    if katakana:
        result = ''.join(chr(ord(c) + 0x60) if 'ぁ' <= c <= 'ゖ' else c for c in result)
    return result


def writing(ids: list[int], instructions: str, characters: dict[int, str], reading: str) -> str:
    """Apply numbered kana insertions between the stored kanji characters.

    Complex optional spellings are retained verbatim in raw_writing and omitted
    from the study pool rather than guessing a Japanese headword.
    """
    ids = [i for i in ids if i]
    if not instructions:
        return ''.join(characters[i] for i in ids) if ids else kana(reading)
    for match in re.finditer(r'#(\d+)#', instructions):
        ids.append(int(match[1]))
    instructions = re.sub(r'#\d+#', '', instructions)
    if re.search(r'[^0-9a-z:^\x27 -]', instructions):
        raise ValueError('Complex writing instruction')
    insertions = {}
    for match in re.finditer(r'(\d)([^0-9]*)', instructions):
        position, text = int(match[1]), match[2]
        if position > len(ids) or position in insertions:
            raise ValueError('Invalid insertion position')
        insertions[position] = kana(text.lstrip('^'), text.startswith('^'))
    return ''.join(insertions.get(i, '') + (characters[ids[i]] if i < len(ids) else '')
                   for i in range(len(ids) + 1))


def plain_meaning(text: str) -> str:
    # Remove links, layout directives, and reading annotations; keep actual glosses.
    text = re.sub(r'\{[^}]*\}', '', text)
    text = re.sub(r'\^[0-9zZ-]+', '', text)
    text = re.sub(r'\*\d+', '', text)
    text = re.sub(r'@\d*(?:\([^)]*\))?', '', text)
    text = re.sub(r'~\d*\]?', '', text)
    text = re.sub(r'[\x00-\x1f]', '', text)
    text = re.sub(r'[&/|+]+', '; ', text)
    text = text.replace('\\', ' ')
    text = re.sub(r'[\\#_*^<>!{}~$=]', '', text)
    text = re.sub(r'\s+', ' ', text)
    text = re.sub(r'\s+([,.;:)\]])', r'\1', text)
    text = re.sub(r'([([])\s+', r'\1', text)
    text = re.sub(r'(;\s*){2,}', '; ', text)
    return text.strip(' ;,')


def clean_reading(raw: str) -> str:
    if '\x01' in raw:
        # Only starred lowercase reading tokens belong on a compact card.
        # Uppercase tokens, name lists, numbers, and reference codes describe
        # grouping and dictionary navigation rather than the primary readings.
        groups = []
        for group in raw.split('|')[0].split('\x01'):
            tokens = [token.strip() for token in group.split('*')
                      if re.fullmatch(r"[a-z][a-z:,;' -]*", token.strip())]
            groups.append(', '.join(dict.fromkeys(tokens)))
        raw = ' / '.join(group for group in groups if group)
    else:
        # Word alternatives and reading annotations follow the first asterisk.
        raw = raw.split('*')[0]
    raw = re.sub(r'([aiueoAIUEO]):', lambda m: dict(zip('aiueoAIUEO', 'āīūēōĀĪŪĒŌ'))[m[1]], raw)
    return re.sub(r'\s+', ' ', raw).strip(' /,;')


def hiragana_reading(reading: str) -> str:
    """Transcribe pronunciation without guessing unrecorded vowel spelling.

    A source macron becomes the phonetic length mark ー, since e.g. ō can
    represent either おう or おお. Literal vowel sequences remain literal.
    """
    parts = re.split(r'([,;/\s]+)', reading)
    result = []
    for part in parts:
        if not part or re.fullmatch(r'[,;/\s]+', part):
            result.append(part)
            continue
        text = part.lower().translate(str.maketrans({'ā': 'a:', 'ī': 'i:', 'ū': 'u:',
                                                     'ē': 'e:', 'ō': 'o:'}))
        # Hepburn's tch denotes a geminated ch, as in itchi (いっち).
        text = text.replace('tch', 'cch')
        try:
            result.append(kana(text))
        except ValueError:
            # Preserve unsupported source material instead of inventing a reading.
            result.append(part)
    return ''.join(result)


READING_REVISION = 1
DEFAULT_JMDICT = Path('Data/JMdict_e.gz')
LONG_VOWELS = {'a': 'あー', 'i': 'いー', 'u': 'うー', 'e': 'えいー', 'o': 'おうー'}
LONG_KANA = {kana[-1]: LONG_VOWELS[roman[-1]] for roman, kana in KANA.items()
             if roman[-1] in LONG_VOWELS and 'ぁ' <= kana[-1] <= 'ゖ'}
KANJI_RUN = re.compile(r'[々〆㐀-鿿豈-﫿\U00020000-\U000323af]+')
# https://kokugo.jitenon.jp/word/p63569
VERIFIED_READINGS = {('旧敵', 'きゅーてき'): 'きゅうてき'}


def as_hiragana(text: str) -> str:
    return ''.join(chr(ord(c) - 0x60) if 'ァ' <= c <= 'ヶ' or c in 'ヽヾ' else c
                   for c in unicodedata.normalize('NFC', text))


def jmdict_readings(reference: Path, spellings: set[str]) -> tuple[dict[str, set[str]], str, str]:
    readings = defaultdict(set)
    compressed = reference.read_bytes()
    with gzip.GzipFile(fileobj=BytesIO(compressed), mode='rb') as source:
        elements = ElementTree.iterparse(source, events=('start', 'end'))
        _, root = next(elements)
        if root.tag != 'JMdict':
            raise ValueError(f'Expected a JMdict XML dictionary in {reference}')
        for event, entry in elements:
            if event != 'end' or entry.tag != 'entry':
                continue
            written = {element.text for element in entry.findall('k_ele/keb')}
            for reading in entry.findall('r_ele'):
                kana = reading.findtext('reb')
                if not kana:
                    continue
                restricted = {element.text for element in reading.findall('re_restr')}
                if not written:
                    applicable = {kana}
                elif reading.find('re_nokanji') is not None:
                    applicable = set()
                else:
                    applicable = written & restricted if restricted else written
                for spelling in applicable & spellings:
                    readings[spelling].add(as_hiragana(kana))
            root.remove(entry)
    return readings, root.get('created', ''), hashlib.sha256(compressed).hexdigest()


def spelling_vowels(spelling: str, choices: list[str]) -> dict[int, set[str]]:
    """Find kana that align to the same reading positions in the written form."""
    spelling = KANJI_RUN.sub('\0', as_hiragana(spelling))
    if not spelling or spelling == '\0':
        return {}
    history = [{0}]
    end = len(choices)
    for char in spelling:
        starts = history[-1]
        if char == '\0':
            ends = set(range(min(starts) + 1, end + 1))
        else:
            ends = {i + 1 for i in starts if i < end and char in choices[i]}
        if not ends:
            return {}
        history.append(ends)
    if end not in history[-1]:
        return {}

    # Walk backward so each position we record is part of a complete alignment.
    marks = [i for i, options in enumerate(choices) if 'ー' in options]
    vowels = {i: set() for i in marks}
    unanchored = set()
    ends = {end}
    for char, starts in zip(reversed(spelling), reversed(history[:-1])):
        if char == '\0':
            last = max(ends)
            ends = {i for i in starts if i < last}
            first = min(ends)
            unanchored.update(i for i in marks if first <= i < last)
        else:
            ends = {i for i in starts if i + 1 in ends and char in choices[i]}
            for i in marks:
                if i in ends:
                    vowels[i].add(char)
    return {i: vowels[i] for i in marks if i not in unanchored}


def reading_choices(reading: str) -> list[str]:
    return [LONG_KANA.get(reading[i - 1], 'ー') if char == 'ー' and i else char
            for i, char in enumerate(reading)]


def reading_matches(reading: str, choices: list[str]) -> bool:
    return len(reading) == len(choices) and all(char in allowed for char, allowed in zip(reading, choices))


def resolve_word_reading(spelling: str, reading: str, references: set[str]) -> tuple[str, bool]:
    verified = VERIFIED_READINGS.get((spelling, reading))
    if verified:
        return verified, False
    marks = [i for i, char in enumerate(reading) if char == 'ー']
    choices = reading_choices(reading)
    written = spelling_vowels(spelling, choices)
    matches = [ref for ref in references
               if reading_matches(ref, choices)
               and all(ref[i] in options for i, options in written.items())]

    result = list(reading)
    unresolved = False
    for i in marks:
        evidence = {ref[i] for ref in matches} if matches else written.get(i, set())
        if len(evidence) == 1:
            result[i] = evidence.pop()
        else:
            unresolved = True
    return ''.join(result), unresolved


def check_word_readings(entries: list[dict], reference: Path) -> dict:
    marked = [entry for entry in entries if entry['kind'] == 'word' and 'ー' in entry['hiragana']]
    readings, created, digest = jmdict_readings(reference, {entry['text'] for entry in marked if entry['text']})
    stats = dict(words_checked=sum(entry['kind'] == 'word' for entry in entries),
                 words_with_length_mark=len(marked), words_corrected=0, words_unresolved=0,
                 study_words_corrected=0, study_words_unresolved=0)
    for entry in marked:
        old = entry['hiragana']
        new, unresolved = resolve_word_reading(entry['text'], old, readings.get(entry['text'], set()))
        entry['hiragana'] = new
        stats['words_corrected'] += new != old
        stats['words_unresolved'] += unresolved
        stats['study_words_corrected'] += entry['eligible'] and new != old
        stats['study_words_unresolved'] += entry['eligible'] and unresolved
    metadata = dict(hiragana_reading_revision=READING_REVISION, hiragana_reference='JMdict',
                    hiragana_reference_sha256=digest,
                    hiragana_word_audit=stats)
    if created:
        metadata['hiragana_reference_date'] = created
    return metadata


def parse_database(data: bytes) -> list[dict]:
    if u32(data, 0) != 0x21430016:
        raise ValueError('Unsupported YARXI database version')
    header = struct.unpack_from('<16I', data)
    kanji_count = (header[4] - header[3]) // 4
    characters = {}
    entries = []
    for index in range(1, kanji_count + 1):
        offset = header[1] + u32(data, header[3] + (index - 1) * 4)
        size = u16(data, offset)
        character = chr(u16(data, offset + 4))
        characters[index] = character
        start = offset + 32 + data[offset + 7] * 2 + (data[offset + 23] & 15)
        fields = data[start:offset + size].split(b'\0')
        reading, summary, meaning = (f.decode('cp1251') for f in fields[:3])
        gloss = plain_meaning(meaning)
        # The short meaning may append an alternate е spelling for search.
        short_meaning = plain_meaning(summary.split('*#*')[0]) or gloss
        entries.append(dict(id=f'kanji-{index}', source_id=index, kind='kanji', text=character,
                            reading=clean_reading(reading), meaning=short_meaning,
                            detail=gloss, strokes=data[offset + 6], raw_reading=reading,
                            raw_meaning=meaning, raw_writing='',
                            eligible=bool(re.search('[А-Яа-яЁё]', short_meaning))))
    offset = header[4]
    for index in range(1, header[14] + 1):
        flags = u32(data, offset)
        size = flags & 0xffff
        if size < 12 or offset + size > header[5]:
            raise ValueError(f'Invalid word record {index}')
        ids = list(struct.unpack_from('<4H', data, offset + 4))
        extra = data[offset + 12 + ((flags >> 24) & 15) * 2:
                     offset + 12 + ((flags >> 24) & 15) * 2 + ((flags >> 16) & 15)]
        start = offset + 12 + ((flags >> 24) & 15) * 2 + len(extra)
        fields = data[start:offset + size].split(b'\0')
        reading, meaning, instruction = (f.decode('cp1251') for f in fields[:3])
        # Russian ё positions are stored separately from the Windows-1251 text.
        positions = set(extra)
        occurrence = 0
        restored = ''
        for char in meaning:
            if char.lower() == 'е':
                occurrence += 1
                if occurrence in positions:
                    char = 'ё' if char.islower() else 'Ё'
            restored += char
        meaning = restored
        gloss = plain_meaning(meaning)
        try:
            text = writing(ids, instruction, characters, reading.split('*')[0])
        except (ValueError, KeyError):
            text = ''
        # Pure cross-references and unresolved optional spellings remain exported,
        # but do not become misleading or empty study cards.
        eligible = bool(text and re.search('[А-Яа-яЁё]', gloss))
        entries.append(dict(id=f'word-{index}', source_id=index, kind='word', text=text,
                            reading=clean_reading(reading), meaning=gloss, detail=gloss,
                            strokes=0, raw_reading=reading, raw_meaning=meaning,
                            raw_writing=instruction, eligible=eligible))
        offset += size
    if offset != header[5]:
        raise ValueError('Word section boundary mismatch')
    for entry in entries:
        entry['hiragana'] = hiragana_reading(entry['reading'])
    return entries


SCHEMA = '''
CREATE TABLE entries (
    id TEXT PRIMARY KEY, source_id INTEGER NOT NULL, kind TEXT NOT NULL,
    text TEXT NOT NULL, reading TEXT NOT NULL, meaning TEXT NOT NULL,
    detail TEXT NOT NULL, strokes INTEGER NOT NULL, raw_reading TEXT NOT NULL,
    raw_meaning TEXT NOT NULL, raw_writing TEXT NOT NULL, eligible INTEGER NOT NULL,
    search_text TEXT NOT NULL, hiragana TEXT NOT NULL
);
CREATE INDEX study_entries ON entries(kind, eligible);
CREATE TABLE metadata (key TEXT PRIMARY KEY, value TEXT NOT NULL);
PRAGMA user_version = 2;
'''


RUSSIAN = re.compile('[А-Яа-яЁё]')
YO_POSITIONS = re.compile(r'\d+(?:,\d+)*')
SQLITE_REFERENCES = re.compile(r'\^+(?:[a-z])?_?\d+(?:[-_:]\d+)*|>+\d+(?:[-:]\d*)*|\*+=\d+')


def sqlite_gloss(raw: str, yo: str = '') -> str:
    if YO_POSITIONS.fullmatch(yo):
        positions = set(map(int, yo.split(',')))
        vowels = count(1)
        raw = re.sub('[еЕ]', lambda m: {'е': 'ё', 'Е': 'Ё'}[m[0]]
                     if next(vowels) in positions else m[0], raw)
    raw = SQLITE_REFERENCES.sub('', raw).replace(r'\-', '-').replace(r'\?', '?')
    return plain_meaning(raw).replace("''", '"')


def sqlite_kana_input(text: str) -> str:
    # Hepburn can write the syllabic n as m before a labial consonant.
    return re.sub('m(?=[bmp])', 'n', text).replace('tch', 'cch')


def read_sqlite_entries(source: Path) -> list[dict]:
    entries = []
    characters = {}
    with closing(sqlite3.connect(source.resolve().as_uri() + '?mode=ro', uri=True)) as db:
        for number, codepoint, strokes, on, kun, nickname, russian in db.execute(
                'SELECT Nomer, Uncd, Str, Onyomi, Kunyomi, RusNick, Russian FROM Kanji ORDER BY Nomer'):
            character = characters[number] = chr(codepoint)
            raw_reading = f'{on}\x01{kun}'
            reading = clean_reading(raw_reading)
            detail = sqlite_gloss(russian)
            meaning = sqlite_gloss(nickname.split('*#*')[0]) or detail
            entries.append(dict(id=f'kanji-yarxi-{number}', source_id=number, kind='kanji',
                                text=character, reading=reading, meaning=meaning, detail=detail or meaning,
                                strokes=strokes, raw_reading=raw_reading, raw_meaning=russian, raw_writing='',
                                hiragana=hiragana_reading(sqlite_kana_input(reading)),
                                eligible=bool(RUSSIAN.search(meaning))))
        for number, k1, k2, k3, k4, instructions, raw_reading, russian, yo in db.execute(
                'SELECT Nomer, K1, K2, K3, K4, Kana, Reading, Russian, Yo FROM Tango ORDER BY Nomer'):
            reading = clean_reading(raw_reading)
            meaning = sqlite_gloss(russian, yo)
            try:
                text = writing([i for i in (k1, k2, k3, k4) if i > 0], sqlite_kana_input(instructions),
                               characters, sqlite_kana_input(raw_reading.split('*')[0]))
            except (ValueError, KeyError):
                text = ''
            entries.append(dict(id=f'word-yarxi-{number}', source_id=number, kind='word', text=text,
                                reading=reading, meaning=meaning, detail=meaning, strokes=0, raw_reading=raw_reading,
                                raw_meaning=russian, raw_writing=instructions,
                                hiragana=hiragana_reading(sqlite_kana_input(reading)),
                                eligible=bool(text and RUSSIAN.search(meaning))))
    return entries


def preserve_entry_ids(entries: list[dict], legacy_path: Path) -> None:
    def key(entry):
        return (entry['kind'], entry['text'], entry['hiragana'] if entry['kind'] == 'word' else '')

    with closing(sqlite3.connect(legacy_path.resolve().as_uri() + '?mode=ro', uri=True)) as db:
        db.row_factory = sqlite3.Row
        metadata = dict(db.execute('SELECT key,value FROM metadata'))
        legacy = [dict(row) for row in db.execute('SELECT * FROM entries WHERE eligible=1')]
    # The original transcription remains the matching key. A refreshed export
    # has modern spelling in the hiragana column, so recover its old reading.
    if 'hiragana_reading_revision' in metadata:
        sqlite_source = ('source_sha256' in metadata and
                         metadata['source_sha256'] == metadata.get('database_sha256'))
        for entry in legacy:
            if entry['kind'] == 'word':
                phonetic = hiragana_reading(entry['reading'])
                if sqlite_source:
                    modern = hiragana_reading(sqlite_kana_input(entry['reading']))
                    if reading_matches(entry['hiragana'], reading_choices(modern)):
                        phonetic = modern
                entry['hiragana'] = phonetic
    legacy_counts = Counter(map(key, legacy))
    candidates = {key(entry): entry for entry in entries if entry['eligible']}
    candidate_counts = Counter(key(entry) for entry in entries if entry['eligible'])
    # Tango.Nomer was renumbered. Match by spelling and pronunciation where
    # both editions have a single match. Keep the old card for ambiguous matches.
    for entry in legacy:
        identity = key(entry)
        if legacy_counts[identity] == candidate_counts[identity] == 1:
            candidates[identity]['id'] = entry['id']
        else:
            entries.append(entry)


def write_sqlite(target: Path, entries: list[dict], **source) -> dict:
    for entry in entries:
        combined = ' '.join(entry[k] for k in ('text', 'reading', 'meaning', 'hiragana'))
        entry['search_text'] = unicodedata.normalize('NFC', ''.join(
            c for c in unicodedata.normalize('NFD', combined.lower())
            if not unicodedata.combining(c) or c in '\u3099\u309a'))
    counts = Counter(e['kind'] for e in entries)
    eligible = Counter(e['kind'] for e in entries if e['eligible'])
    mark_policy = ('verified word spelling, phonetic fallback' if 'hiragana_reading_revision' in source
                   else 'phonetic length mark')
    manifest = dict(**source, translation_language='ru',
                    reading_formats=['hiragana', 'romaji'], hiragana_long_vowels=mark_policy,
                    counts=counts, study_counts=eligible, schema_version=2)
    target.parent.mkdir(parents=True, exist_ok=True)
    with tempfile.NamedTemporaryFile(dir=target.parent, suffix='.sqlite.tmp', delete=False) as file:
        temporary = Path(file.name)
    try:
        with closing(sqlite3.connect(temporary)) as db, db:
            db.executescript(SCHEMA)
            db.executemany('INSERT INTO entries VALUES (:id,:source_id,:kind,:text,:reading,:meaning,'
                           ':detail,:strokes,:raw_reading,:raw_meaning,:raw_writing,:eligible,:search_text,:hiragana)', entries)
            db.executemany('INSERT INTO metadata VALUES (?,?)',
                           [(key, json.dumps(value, ensure_ascii=False)) for key, value in manifest.items()])
            if db.execute('PRAGMA integrity_check').fetchone()[0] != 'ok':
                raise ValueError('SQLite integrity check failed')
        temporary.replace(target)
    finally:
        temporary.unlink(missing_ok=True)
    return manifest


def export(image: Path, output: Path, reference: Path = DEFAULT_JMDICT) -> dict:
    raw = extract_file(read_image(image), 'yarxice.db')
    entries = parse_database(raw)
    readings = check_word_readings(entries, reference)
    manifest = write_sqlite(output / 'dictionary.sqlite', entries, source=image.name, **readings,
                            source_sha256=hashlib.sha256(image.read_bytes()).hexdigest(),
                            database_sha256=hashlib.sha256(raw).hexdigest())
    extracted = output / 'yarxice.db'
    if not extracted.is_file() or extracted.read_bytes() != raw:
        extracted.write_bytes(raw)
    (output / 'manifest.json').write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + '\n',
                                          encoding='utf-8', newline='\n')
    return manifest


def ensure_legacy_export(image: Path, output: Path, reference: Path) -> dict | None:
    target = output / 'dictionary.sqlite'
    if target.is_file():
        with closing(sqlite3.connect(target.resolve().as_uri() + '?mode=ro', uri=True)) as db:
            revision = db.execute("SELECT value FROM metadata WHERE key='hiragana_reading_revision'").fetchone()
        if revision == (str(READING_REVISION),):
            return None
    return export(image, output, reference)


def export_windows(source: Path, legacy: Path, output: Path, reference: Path = DEFAULT_JMDICT) -> dict:
    target = output / 'dictionary.windows.sqlite'
    if target.resolve() in (source.resolve(), legacy.resolve()):
        raise ValueError('The Windows export must have a separate path from its input databases')
    entries = read_sqlite_entries(source)
    preserve_entry_ids(entries, legacy)
    readings = check_word_readings(entries, reference)
    source_hash = hashlib.sha256(source.read_bytes()).hexdigest()
    return write_sqlite(target, entries, source=source.name, source_sha256=source_hash,
                        database_sha256=source_hash, **readings)


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('image', nargs='?', type=Path, default=Path('YARXI.dmg'))
    parser.add_argument('--output', type=Path, default=Path('Data'))
    mode = parser.add_mutually_exclusive_group()
    mode.add_argument('--sqlite-source', type=Path, help='Export a modern yarxi.db for Windows')
    mode.add_argument('--ensure-legacy-export', action='store_true',
                      help='Regenerate the disk image export only if its reading revision is outdated')
    parser.add_argument('--legacy', type=Path, default=Path('Data/dictionary.sqlite'),
                        help='Previous Kotoba export for preserving IDs during the Windows export')
    parser.add_argument('--jmdict', type=Path, default=DEFAULT_JMDICT,
                        help='Local JMdict_e.gz for verifying word readings (default: %(default)s)')
    args = parser.parse_args()
    try:
        if args.sqlite_source:
            manifest = export_windows(args.sqlite_source, args.legacy, args.output, args.jmdict)
        elif args.ensure_legacy_export:
            manifest = ensure_legacy_export(args.image, args.output, args.jmdict)
        else:
            manifest = export(args.image, args.output, args.jmdict)
    except FileNotFoundError as error:
        parser.exit(1, f'Local dictionary file required: {error.filename}. See Data/README.md.\n')
    except (ElementTree.ParseError, gzip.BadGzipFile, EOFError) as error:
        parser.exit(1, f'Cannot read JMdict from {args.jmdict}: {error}\n')
    if manifest:
        print(json.dumps(manifest, ensure_ascii=False, indent=2))
