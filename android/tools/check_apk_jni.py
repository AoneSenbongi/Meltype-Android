"""Check the Release APK's native entry points before distributing it.

This project uses .NET Android's Release marshal methods. A successful build
alone does not prove that the generated Java callbacks have native targets.
Uses only Python's standard library.
"""
import pathlib
import struct
import sys
import zipfile


def exported_symbols(data):
    if data[:6] != b'\x7fELF\x02\x01':
        raise ValueError('Expected a little-endian ELF64 shared library')
    section_offset = struct.unpack_from('<Q', data, 40)[0]
    section_size, section_count = struct.unpack_from('<HH', data, 58)
    sections = [struct.unpack_from('<IIQQQQIIQQ', data, section_offset + i * section_size)
                for i in range(section_count)]
    names = set()
    for section in sections:
        if section[1] != 11:  # SHT_DYNSYM
            continue
        strings_section = sections[section[6]]
        strings = data[strings_section[4]:strings_section[4] + strings_section[5]]
        for offset in range(section[4], section[4] + section[5], section[9]):
            name_offset, info, visibility, index, value, size = struct.unpack_from('<IBBHQQ', data, offset)
            if index and info >> 4 in (1, 2):  # Defined GLOBAL/WEAK symbol
                end = strings.find(b'\0', name_offset)
                names.add(strings[name_offset:end].decode('utf-8'))
    return names


def check(apk):
    failures = []
    with zipfile.ZipFile(apk) as archive:
        for abi in ('arm64-v8a', 'x86_64'):
            symbols = exported_symbols(archive.read(f'lib/{abi}/libxamarin-app.so'))
            required = (
                'Java_jp_aonesenbongi_meltype_MainActivity_n_1onCreate',
                'Java_jp_aonesenbongi_meltype_MainActivity_n_1onResume',
                'Java_crc64cc9ac1f257f62241_KeyboardService_n_1onCreate',
                'Java_crc64cc9ac1f257f62241_KeyboardService_n_1onCreateInputView',
            )
            missing = [name for name in required if name not in symbols]
            if missing:
                failures.append(f'{abi}: missing ' + ', '.join(missing))
            else:
                print(f'PASS: {abi} activity and keyboard JNI callbacks')
    if failures:
        raise AssertionError('\n'.join(failures))


if __name__ == '__main__':
    check(pathlib.Path(sys.argv[1]))
