#!/usr/bin/env python3
"""Export this YARXI disk image without mounting or launching its application.

Only Python's standard library is required. The UDIF/HFS+ reader deliberately
fails on unsupported compression or overflow extents instead of truncating data.
"""
from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path
import plistlib
import re
import sqlite3
import struct
import unicodedata
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


def export(image: Path, output: Path) -> dict:
    raw = extract_file(read_image(image), 'yarxice.db')
    entries = parse_database(raw)
    for entry in entries:
        combined = ' '.join(entry[k] for k in ('text', 'reading', 'meaning', 'hiragana'))
        entry['search_text'] = unicodedata.normalize('NFC', ''.join(
            c for c in unicodedata.normalize('NFD', combined.lower())
            if not unicodedata.combining(c) or c in '\u3099\u309a'))
    output.mkdir(parents=True, exist_ok=True)
    (output / 'yarxice.db').write_bytes(raw)
    target = output / 'dictionary.sqlite'
    temporary = target.with_suffix('.sqlite.tmp')
    temporary.unlink(missing_ok=True)
    counts = {kind: sum(e['kind'] == kind for e in entries) for kind in ('kanji', 'word')}
    eligible = {kind: sum(e['kind'] == kind and e['eligible'] for e in entries) for kind in counts}
    manifest = dict(source=image.name, source_sha256=hashlib.sha256(image.read_bytes()).hexdigest(),
                    database_sha256=hashlib.sha256(raw).hexdigest(), translation_language='ru',
                    reading_formats=['hiragana', 'romaji'], hiragana_long_vowels='phonetic length mark',
                    counts=counts, study_counts=eligible, schema_version=2)
    with sqlite3.connect(temporary) as db:
        db.executescript(SCHEMA)
        db.executemany('INSERT INTO entries VALUES (:id,:source_id,:kind,:text,:reading,:meaning,'
                       ':detail,:strokes,:raw_reading,:raw_meaning,:raw_writing,:eligible,:search_text,:hiragana)', entries)
        db.executemany('INSERT INTO metadata VALUES (?,?)',
                       [(key, json.dumps(value, ensure_ascii=False)) for key, value in manifest.items()])
        if db.execute('PRAGMA integrity_check').fetchone()[0] != 'ok':
            raise ValueError('SQLite integrity check failed')
    temporary.replace(target)
    (output / 'manifest.json').write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + '\n')
    return manifest


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('image', nargs='?', type=Path, default=Path('YARXI.dmg'))
    parser.add_argument('--output', type=Path, default=Path('Data'))
    args = parser.parse_args()
    print(json.dumps(export(args.image, args.output), ensure_ascii=False, indent=2))
