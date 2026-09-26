import sys
import struct
import tempfile
import unittest
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "engine"))
import mov_accessibility as mov


def track(media):
    header = bytearray(84)
    header[3] = 3
    return mov.atom(b"trak", mov.atom(b"tkhd", header) + mov.atom(b"mdia", mov.atom(b"hdlr", bytes(8) + media + bytes(12))))


class MetadataTests(unittest.TestCase):
    def test_tags_groups_and_media_offsets(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "export.mov"
            movie = track(b"vide") + track(b"soun") + track(b"soun") + track(b"sbtl")
            original = mov.atom(b"ftyp", b"qt  ") + mov.atom(b"moov", movie) + mov.atom(b"mdat", b"UNCHANGED MEDIA")
            path.write_bytes(original)
            mov.finalize(path)
            result = path.read_bytes()
            self.assertEqual(result.index(b"UNCHANGED MEDIA"), original.index(b"UNCHANGED MEDIA"))
            self.assertEqual(result.count(b"UNCHANGED MEDIA"), 1)
            self.assertEqual(result.count(mov.DESCRIPTION), 1)
            self.assertEqual(result.count(mov.TRANSCRIPT), 1)
            updated = next(value for kind, value in mov.children(result) if kind == b"moov")
            groups = []
            for kind, payload in mov.children(updated):
                if kind == b"trak":
                    header = next(value for child, value in mov.children(payload) if child == b"tkhd")
                    groups.append(struct.unpack_from(">h", header, 34)[0])
            self.assertEqual(groups, [0, 1, 1, 2])
            mov.finalize(path)
            self.assertEqual(path.read_bytes(), result)

    def test_invalid_input_is_unchanged(self):
        for contents in [b"bad", mov.atom(b"moov", track(b"soun")), struct.pack(">I4s", 0, b"mdat")]:
            with self.subTest(contents=contents), tempfile.TemporaryDirectory() as directory:
                path = Path(directory) / "invalid.mov"
                path.write_bytes(contents)
                with self.assertRaises(ValueError): mov.finalize(path)
                self.assertEqual(path.read_bytes(), contents)

    def test_bad_nested_atom_is_rejected(self):
        with self.assertRaises(ValueError): list(mov.children(struct.pack(">I4s", 999, b"trak")))
