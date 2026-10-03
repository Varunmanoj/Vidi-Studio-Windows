"""Portable QuickTime track characteristics; never changes encoded media bytes.

Format: https://developer.apple.com/documentation/quicktime-file-format/user_data_atoms
Only used on a not-yet-published export. Replacing moov with free and appending
its updated copy preserves every media/chunk offset (including large files).
"""
import struct
from pathlib import Path

DESCRIPTION = b"public.accessibility.describes-video"
TRANSCRIPT = b"public.accessibility.transcribes-spoken-dialog"
MAX_HEADER = 64 * 1024 * 1024


def atom(kind, payload):
    return struct.pack(">I4s", len(payload) + 8, kind) + payload


def children(data):
    position = 0
    while position < len(data):
        if len(data) - position < 8:
            raise ValueError("Truncated QuickTime atom header.")
        size, kind = struct.unpack_from(">I4s", data, position)
        header = 8
        if size == 1:
            if len(data) - position < 16:
                raise ValueError("Truncated extended QuickTime atom.")
            size = struct.unpack_from(">Q", data, position + 8)[0]
            header = 16
        if size == 0:
            size = len(data) - position
        if size < header or position + size > len(data):
            raise ValueError("Invalid QuickTime atom bounds.")
        yield kind, data[position + header:position + size]
        position += size


def track_type(track):
    for kind, payload in children(track):
        if kind == b"mdia":
            for child, value in children(payload):
                if child == b"hdlr":
                    if len(value) < 12:
                        raise ValueError("Invalid QuickTime media handler.")
                    return value[8:12]
    return None


def update_track(track, characteristic, group):
    characteristics = (characteristic,) if isinstance(characteristic, bytes) else (characteristic or ())
    output = []
    found_userdata = False
    for kind, payload in children(track):
        if kind == b"tkhd":
            if not payload or payload[0] not in (0, 1):
                raise ValueError("Unsupported QuickTime track header.")
            offset = 34 if payload[0] == 0 else 46
            if len(payload) < offset + 2:
                raise ValueError("Truncated QuickTime track header.")
            payload = payload[:offset] + struct.pack(">h", group) + payload[offset + 2:]
        if kind == b"udta":
            if found_userdata:
                raise ValueError("Duplicate QuickTime track user data.")
            found_userdata = True
            entries = list(children(payload))
            for value in characteristics:
                if not any(k == b"tagc" and v == value for k, v in entries):
                    entries.append((b"tagc", value))
            payload = b"".join(atom(k, v) for k, v in entries)
        output.append(atom(kind, payload))
    if characteristics and not found_userdata:
        output.append(atom(b"udta", b"".join(atom(b"tagc", value) for value in characteristics)))
    return b"".join(output)


def tag_movie(movie, described_audio_indices=None, described_subtitle_indices=()):
    result = []
    audio_count = 0
    subtitle_count = 0
    for kind, payload in children(movie):
        if kind == b"trak":
            media = track_type(payload)
            if media == b"soun":
                audio_count += 1
                described = audio_count == 2 if described_audio_indices is None else audio_count - 1 in described_audio_indices
                payload = update_track(payload, DESCRIPTION if described else None, 1)
            elif media in (b"text", b"sbtl", b"subt", b"clcp"):
                characteristics = (TRANSCRIPT, DESCRIPTION) if subtitle_count in described_subtitle_indices else (TRANSCRIPT,)
                payload = update_track(payload, characteristics, 2)
                subtitle_count += 1
        result.append(atom(kind, payload))
    if described_audio_indices is None and audio_count != 2:
        raise ValueError("Accessibility tagging requires exactly two audio tracks.")
    if described_audio_indices is not None and any(index < 0 or index >= audio_count for index in described_audio_indices):
        raise ValueError("Described audio track index is outside the movie.")
    if any(index < 0 or index >= subtitle_count for index in described_subtitle_indices):
        raise ValueError("Descriptive subtitle track index is outside the movie.")
    return b"".join(result)


def finalize(path, described_audio_indices=None, described_subtitle_indices=()):
    """Tag a newly generated MOV before the engine commits its export."""
    path = Path(path)
    with path.open("r+b") as file:
        length = file.seek(0, 2)
        offset = 0
        movies = []
        while offset < length:
            file.seek(offset)
            header = file.read(16)
            if len(header) < 8:
                raise ValueError("Truncated QuickTime file.")
            size, kind = struct.unpack_from(">I4s", header)
            header_size = 8
            if size == 1:
                if len(header) < 16:
                    raise ValueError("Truncated extended atom.")
                size = struct.unpack_from(">Q", header, 8)[0]
                header_size = 16
            if size == 0:
                raise ValueError("Cannot append metadata after an unbounded atom.")
            if size < header_size or offset + size > length:
                raise ValueError("Invalid QuickTime file bounds.")
            if kind == b"moov":
                if size > MAX_HEADER:
                    raise ValueError("QuickTime metadata exceeds the supported size.")
                file.seek(offset + header_size)
                movies.append((offset, file.read(size - header_size)))
            elif kind == b"moof":
                raise ValueError("Fragmented movies are not supported by this metadata writer.")
            offset += size
        if len(movies) != 1:
            raise ValueError("Expected one QuickTime movie header.")
        position, old = movies[0]
        updated = tag_movie(old, described_audio_indices, described_subtitle_indices)
        if updated == old:
            return
        file.seek(length)
        file.write(atom(b"moov", updated))
        file.flush()
        file.seek(position + 4)
        file.write(b"free")
        file.flush()
