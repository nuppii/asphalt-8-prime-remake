"""Strict, lossless reader/writer for observed Asphalt 8 4.8.0 GUI BGIN trees.

Unknown property types fail closed. Property bytes remain opaque unless explicitly
edited; no heuristically identified byte sequence is treated as a child node.
"""
from dataclasses import dataclass, field
import copy
import struct


class FormatError(ValueError):
    pass


def packed_string(value):
    value = value.encode("utf-8") if isinstance(value, str) else value
    if len(value) > 65535:
        raise ValueError("BGIN strings are limited to 65535 bytes")
    return struct.pack("<H", len(value)) + value


class Reader:
    def __init__(self, data):
        self.data, self.pos = data, 0

    def take(self, count):
        if count < 0 or self.pos + count > len(self.data):
            raise FormatError("Truncated data at 0x%x" % self.pos)
        result = self.data[self.pos:self.pos + count]
        self.pos += count
        return result

    def unpack(self, fmt):
        return struct.unpack(fmt, self.take(struct.calcsize(fmt)))

    def string(self):
        return self.take(self.unpack("<H")[0])


@dataclass
class Property:
    key: int
    kind: int
    count: int
    payload: bytes

    def encode(self):
        return struct.pack("<IHH", self.key, self.kind, self.count) + self.payload

    def set_string(self, value):
        if self.kind not in (8, 0x400) or self.count != 1:
            raise ValueError("Property is not a scalar string/resource reference")
        self.payload = packed_string(value)

    def set_number(self, value):
        if self.kind != 2 or self.count != 1:
            raise ValueError("Property is not a scalar double")
        self.payload = struct.pack("<d", value)


@dataclass
class Node:
    kind: int
    name: bytes
    transform: bytes
    properties: list = field(default_factory=list)
    children: list = field(default_factory=list)

    def encode(self):
        payload = (packed_string(self.name) + self.transform +
                   struct.pack("<H", len(self.properties)) +
                   b"".join(p.encode() for p in self.properties) +
                   struct.pack("<H", len(self.children)) +
                   b"".join(c.encode() for c in self.children))
        return struct.pack("<II", self.kind, len(payload)) + payload

    def walk(self):
        yield self
        for child in self.children:
            yield from child.walk()

    def find(self, name):
        target = name.encode() if isinstance(name, str) else name
        matches = [n for n in self.walk() if n.name == target]
        if len(matches) != 1:
            raise ValueError("Expected one node %r; found %d" % (name, len(matches)))
        return matches[0]

    def clone(self, name):
        result = copy.deepcopy(self)
        result.name = name.encode() if isinstance(name, str) else name
        return result

    def property(self, key):
        matches = [p for p in self.properties if p.key == key]
        if len(matches) != 1:
            raise ValueError("Expected one property 0x%x" % key)
        return matches[0]


def read_node(reader):
    start = reader.pos
    kind, size = reader.unpack("<II")
    end = start + 8 + size
    if end > len(reader.data):
        raise FormatError("Node exceeds file at 0x%x" % start)
    name = reader.string()
    transform = reader.take(20)  # Five native float values; preserve bit patterns.
    props = []
    for _ in range(reader.unpack("<H")[0]):
        key, typ, count = reader.unpack("<IHH")
        value_start = reader.pos
        for _ in range(count):
            if typ == 2:
                reader.take(8)
            elif typ == 4:
                # Observed enum value: int32 plus a type hash and string payload.
                reader.take(8)
                reader.string()
            elif typ in (8, 0x400):
                reader.string()
            elif typ == 0x80:
                reader.take(reader.unpack("<B")[0] * 4)
            else:
                raise FormatError("Unknown property type 0x%x at 0x%x" % (typ, value_start))
        props.append(Property(key, typ, count, reader.data[value_start:reader.pos]))
    children = [read_node(reader) for _ in range(reader.unpack("<H")[0])]
    if reader.pos != end:
        raise FormatError("Node %r ends at 0x%x, expected 0x%x" % (name, reader.pos, end))
    return Node(kind, name, transform, props, children)


@dataclass
class Document:
    magic: bytes
    namespace: bytes
    records: list  # Ordered (native name hash, Node) pairs.

    @classmethod
    def parse(cls, data):
        reader = Reader(data)
        magic = reader.take(8)
        if magic != bytes.fromhex("aa1a0c0030a55694"):
            raise FormatError("Unsupported BGIN signature")
        namespace = reader.string()
        size = reader.unpack("<I")[0]
        count = reader.unpack("<H")[0]
        directory = [reader.unpack("<II") for _ in range(count)]
        reserved, repeated_count = reader.unpack("<HH")
        if reserved != 0 or repeated_count != count:
            raise FormatError("Unexpected directory trailer")
        if size != count * 8 + 4:
            raise FormatError("Unexpected directory length")
        records = []
        for key, offset in directory:
            if reader.pos != offset:
                raise FormatError("Unexpected record offset/gap at 0x%x" % offset)
            records.append((key, read_node(reader)))
        if reader.pos != len(data):
            raise FormatError("Unexpected trailing bytes")
        return cls(magic, namespace, records)

    def encode(self):
        prefix = self.magic + packed_string(self.namespace)
        directory_size = 4 + len(self.records) * 8
        offset = len(prefix) + 6 + len(self.records) * 8 + 4
        directory, payload = [], []
        for key, node in self.records:
            encoded = node.encode()
            directory.append(struct.pack("<II", key, offset))
            payload.append(encoded)
            offset += len(encoded)
        return (prefix + struct.pack("<IH", directory_size, len(self.records)) +
                b"".join(directory) + struct.pack("<HH", 0, len(self.records)) + b"".join(payload))

    def root(self, name):
        matches = [node for _, node in self.records if node.name == name.encode()]
        if len(matches) != 1:
            raise ValueError("Expected one top-level node %r" % name)
        return matches[0]
