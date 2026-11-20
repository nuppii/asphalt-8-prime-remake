"""Integration tests against locally installed original resources (not fixtures)."""
import argparse
import struct
import unittest
from pathlib import Path
from bgin import Document, FormatError
from build_native_ui import add_entry_card, add_rule_checkboxes, clone_native_row, build_keyboard_document, build_panel_document, build_roster_document


GUI = None


class NativeResourceTests(unittest.TestCase):
    def load(self, name):
        data = (GUI / (name + ".bgin")).read_bytes()
        return data, Document.parse(data)

    def test_lossless_original_trees(self):
        for name in ["multiplayer_menu", "windows_contents", "background_posters_templates"]:
            with self.subTest(name=name):
                data, document = self.load(name)
                self.assertEqual(document.encode(), data)

    def test_cloned_card_and_checkboxes_survive_real_serialization(self):
        original, document = self.load("multiplayer_menu")
        _, controls = self.load("windows_contents")
        unchanged = [(key, node.encode()) for key, node in document.records
                     if node.name != b"screen_multiplayer_private_local"]
        add_entry_card(document, "CUSTOM ROOM SETTINGS")
        add_rule_checkboxes(document, controls)
        encoded = document.encode()
        self.assertGreater(len(encoded), len(original))
        reparsed = Document.parse(encoded)
        self.assertEqual(encoded, reparsed.encode())
        root = reparsed.root("screen_multiplayer_private_local")
        card = root.find("offline_room_settings_card")
        self.assertEqual(card.find("offline_room_settings_button").kind, 0x7D332216)
        self.assertIn(b"CUSTOM ROOM SETTINGS|", card.encode())
        checks = root.find("offline_room_rules_panel").children
        self.assertEqual(len(checks), 6)
        for check in checks:
            self.assertIn(b"Movie_igm_checker_off", check.encode())
            self.assertIn(b"Movie_igm_checker_on", check.encode())
        self.assertEqual(unchanged, [(key, node.encode()) for key, node in reparsed.records
                                    if node.name != b"screen_multiplayer_private_local"])
        # Parser independently verifies every declared node length and child count
        # and every absolute directory offset after larger variable-length strings.
        self.assertNotEqual(original, encoded)

    def test_standalone_keyboard_native_buttons_and_directory(self):
        original, document = self.load("multiplayer_menu")
        controls_original, controls = self.load("windows_contents")
        keyboard = build_keyboard_document(document, controls)
        self.assertEqual(document.encode(), original)
        self.assertEqual(controls.encode(), controls_original)
        decoded = Document.parse(keyboard.encode())
        self.assertEqual(decoded.namespace, b"offline_lobby_ui")
        self.assertEqual(len(decoded.records), 1)
        original_key = next(key for key, node in document.records
                            if node.name == b"screen_multiplayer_private_local")
        self.assertEqual(decoded.records[0][0], original_key)
        root = decoded.root("screen_multiplayer_private_local")
        frame = root.find("offline_keyboard_frame")
        for key, value in [(0x7766BD8A, 90), (0xB6490172, 90), (0x94DE0460, 0)]:
            self.assertEqual(struct.unpack("<d", frame.property(key).payload)[0], value)
        buttons = [node for node in root.walk() if node.kind == 0x7D332216]
        self.assertEqual(len(buttons), 49)
        expected = ["offline_key_" + key for key in
                    list("ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789") +
                    ["SPACE", "BACKSPACE", "CLEAR", "CANCEL", "CONFIRM", "SHIFT", "PASTE",
                     "DASH", "DOT", "UNDERSCORE", "COLON", "SLASH"]]
        for name in expected + ["offline_input_value"]:
            button = root.find(name)
            self.assertEqual(button.kind, 0x7D332216)
            self.assertIn(b"Movie_common_button_orange_normal", button.encode())
            self.assertIn(b"Movie_common_button_orange_pressed", button.encode())
            self.assertIn(b"/Widgets/MM_White_Text_small", button.encode())
        self.assertEqual(decoded.encode(), keyboard.encode())
        for name, value in [("DASH", "-"), ("DOT", "."), ("UNDERSCORE", "_"),
                            ("COLON", ":"), ("SLASH", "/")]:
            prop = root.find("offline_key_" + name).property(0x48B93E13)
            self.assertEqual(prop.payload[2:], (value + "|").encode())

    def test_blank_vertical_native_panel(self):
        original, document = self.load("multiplayer_menu")
        _, generic = self.load("generic_templates")
        _, events = self.load("cmp_special_events")
        panel = build_panel_document(document, generic, events)
        self.assertEqual(document.encode(), original)
        decoded = Document.parse(panel.encode())
        self.assertEqual(decoded.namespace, b"offline_lobby_panel")
        self.assertEqual(len(decoded.records), 2)
        root = decoded.root("screen_multiplayer_private_local")
        self.assertEqual(root.kind, 0x8706BEAF)
        self.assertEqual(len(root.children), 1)
        frame = root.find("offline_panel_frame")
        self.assertEqual(frame.kind, 0x02737331)
        self.assertEqual(struct.unpack("<d", frame.property(0x7766BD8A).payload)[0], 90)
        self.assertEqual(struct.unpack("<d", frame.property(0xB6490172).payload)[0], 90)
        self.assertEqual(struct.unpack("<d", frame.property(0x94DE0460).payload)[0], 0)
        rows = root.find("offline_panel_rows")
        self.assertEqual(rows.kind, 0xDE8925BA)
        self.assertEqual(rows.children, [])
        self.assertEqual(frame.children, [rows])
        search_row = decoded.root("template_bottom_bar_button").find("offline_directory_row")
        self.assertEqual([child.name for child in search_row.children], [b"offline_room_lock", b"tab_button"])
        icon = search_row.find("offline_room_lock")
        self.assertEqual(icon.kind, 0x6E739C13)
        self.assertIn(b"/Sprites/UI_cmp/Movie_cmp_lock_icon", icon.encode())
        self.assertEqual(struct.unpack("<Bff", icon.property(0x71B64C87).payload), (2, 24, 24))
        self.assertEqual(decoded.encode(), panel.encode())

    def test_waiting_room_roster_has_eight_cells_and_pager(self):
        original, document = self.load("multiplayer_menu")
        _, controls = self.load("windows_contents")
        roster = Document.parse(build_roster_document(document, controls).encode())
        root = roster.root("screen_mp_private_lobby_waitroom")
        self.assertEqual([len(row.children) for row in root.children], [4, 4, 3])
        for index in range(8):
            root.find("offline_roster_slot_" + str(index))
        for name in ("prev", "status", "next"):
            root.find("offline_roster_" + name)
        self.assertEqual(document.encode(), original)

    def test_fail_closed_on_corruption(self):
        data, document = self.load("multiplayer_menu")
        for corrupt in [data[:-1], b"INVALID!" + data[8:]]:
            with self.assertRaises(FormatError):
                Document.parse(corrupt)
        node = document.records[0][1]
        node.properties[0].kind = 0xFFFF
        with self.assertRaises(FormatError):
            Document.parse(document.encode())

    def test_native_room_list_row_clone(self):
        _, document = self.load("multiplayer_menu")
        root = document.root("screen_mp_private_lobby")
        source = root.find("room_row_template004")
        clone_native_row(document, "screen_mp_private_lobby", "room_row_template004",
                         "room_scroll", "offline_additional_room_row")
        reparsed = Document.parse(document.encode())
        row = reparsed.root("screen_mp_private_lobby").find("offline_additional_room_row")
        self.assertEqual(row.kind, source.kind)
        self.assertEqual([child.encode() for child in row.children],
                         [child.encode() for child in source.children])


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--gui", required=True, type=Path)
    args, remaining = parser.parse_known_args()
    GUI = args.gui
    unittest.main(argv=[__file__] + remaining)
