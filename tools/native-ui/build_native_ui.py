"""Build native UI mutations from the user's installed assets; never bundle assets."""
import argparse
import json
import struct
from pathlib import Path
from bgin import Document


TEXT = 0x48B93E13


def unconstrained_frame(frame):
    """Use 90% in each native axis, disabling the copied poster aspect lock."""
    frame.property(0x7766BD8A).set_number(90.0)
    frame.property(0xB6490172).set_number(90.0)
    frame.property(0x94DE0460).set_number(0.0)
    return frame


def parent_of(root, node):
    return next(parent for parent in root.walk() if node in parent.children)


def add_entry_card(document, label="ROOM SETTINGS"):
    """Duplicate the original poster, button, title and art in the native row.

    This is a presentation mutation only. Native controller callback registration
    for offline_room_settings_button remains required by the runtime integrator.
    """
    root = document.root("screen_multiplayer_private_local")
    existing = root.find("mm_create_room_template000")
    row = parent_of(root, existing)
    if row.kind != 0xB490D6D6 or len(row.children) != 3:
        raise ValueError("Unsupported original entry row layout")
    if row.children[1].kind != 0x64BAC2F5:
        raise ValueError("Expected original horizontal spacer")
    card = existing.clone("offline_room_settings_card")
    card.find("create_button").name = b"offline_room_settings_button"
    card.find("offer_label").property(TEXT).set_string(label + "|")
    row.children.extend([row.children[1].clone("offline_settings_spacer"), card])
    return card


def add_rule_checkboxes(document, controls_document):
    """Append native checkboxes to a cloned native vertical layout container.

    The generated panel is attached to the entry screen's native vertical center.
    Its final placement/visibility must be verified in-game before release.
    """
    root = document.root("screen_multiplayer_private_local")
    center = root.find("center_mp_done")
    panel = center.clone("offline_room_rules_panel")
    panel.children = []
    rules = [
        ("allow_mutators", "ALLOW MUTATORS"),
        ("allow_cheats", "ALLOW CHEATS"),
        ("require_owned_cars", "REQUIRE OWNED CARS"),
        ("ignore_class", "IGNORE CLASS"),
        ("fill_bots", "FILL EMPTY SLOTS WITH BOTS"),
        ("private_room", "PRIVATE ROOM"),
    ]
    template = controls_document.root("windows8_checkbox_simple")
    for key, label in rules:
        check = template.clone("offline_" + key + "_row")
        check.find("horizon_label").name = ("offline_" + key + "_label").encode()
        check.find("offline_" + key + "_label").property(TEXT).set_string(label + "|")
        check.find("horizon_button_checkbox").name = ("offline_" + key + "_checkbox").encode()
        panel.children.append(check)
    center.children.append(panel)
    return panel


def clone_native_row(document, screen, source, parent, name):
    """Clone an existing native list row or control into a named native parent."""
    root = document.root(screen)
    node = root.find(source).clone(name)
    root.find(parent).children.append(node)
    return node


def build_keyboard_document(document, controls_document):
    """Standalone native keyboard, retaining an observed root name/hash pair.

    Namespace/file is offline_lobby_ui; root name deliberately remains original
    so no unknown native hash algorithm is guessed for the directory entry.
    """
    source_root = document.root("screen_multiplayer_private_local")
    key = next(key for key, node in document.records if node is source_root)
    root = source_root.clone("screen_multiplayer_private_local")
    frame = unconstrained_frame(source_root.find("Fixed Size Container000").clone("offline_keyboard_frame"))
    vertical = source_root.find("center_mp_done").clone("offline_keyboard_rows")
    vertical.children = []
    horizontal = source_root.find("mm_create_room_template000").children[1]
    if horizontal.kind != 0xB490D6D6:
        raise ValueError("Expected native horizontal container")
    button_source = controls_document.root("input_tranfer_code_screen").find("paste_button")

    def button(name, title):
        result = button_source.clone(name)
        result.property(TEXT).set_string(title + "|")
        # Original transfer button padding is 5/5/20/20, unsuitable for small
        # keys. This is the same observed double-array field, preserved type.
        result.property(0xC8176D90).payload = struct.pack("<dddd", 0, 0, 0, 0)
        return result

    display = button("offline_input_value", "ENTER TEXT")
    vertical.children.append(display)
    for index, letters in enumerate(["0123456789", "QWERTYUIOP", "ASDFGHJKL", "ZXCVBNM"]):
        row = horizontal.clone("offline_keyboard_row_" + str(index))
        row.children = [button("offline_key_" + char, char) for char in letters]
        vertical.children.append(row)
    punctuation = horizontal.clone("offline_keyboard_punctuation")
    punctuation.children = [button("offline_key_" + key, title) for key, title in
                            [("SHIFT", "CAPS"), ("DASH", "-"), ("DOT", "."),
                             ("UNDERSCORE", "_"), ("COLON", ":"), ("SLASH", "/"),
                             ("PASTE", "PASTE")]]
    vertical.children.append(punctuation)
    actions = horizontal.clone("offline_keyboard_actions")
    actions.children = [button("offline_key_" + key, title) for key, title in
                        [("SPACE", "SPACE"), ("BACKSPACE", "DELETE"), ("CLEAR", "CLEAR"),
                         ("CANCEL", "CANCEL"), ("CONFIRM", "CONFIRM")]]
    vertical.children.append(actions)
    frame.children = [vertical]
    root.children = [frame]
    return Document(document.magic, b"offline_lobby_ui", [(key, root)])


def build_panel_document(document):
    """Blank original vertical container for runtime-created native buttons."""
    source = document.root("screen_multiplayer_private_local")
    key = next(key for key, node in document.records if node is source)
    root = source.clone("screen_multiplayer_private_local")
    frame = unconstrained_frame(source.find("Fixed Size Container000").clone("offline_panel_frame"))
    rows = source.find("center_mp_done").clone("offline_panel_rows")
    rows.children = []
    frame.children = [rows]
    root.children = [frame]
    return Document(document.magic, b"offline_lobby_panel", [(key, root)])


def build_roster_document(document, controls_document):
    """Eight reusable native cells and a footer for the waiting-room roster."""
    source = document.root("screen_mp_private_lobby_waitroom")
    key = next(key for key, node in document.records if node is source)
    rows = source.find("player_list").clone(source.name)
    horizontal = source.find("upper_player_row")
    button = controls_document.root("input_tranfer_code_screen").find("paste_button")

    def cell(name, title):
        item = button.clone(name)
        item.property(TEXT).set_string(title + "|")
        item.property(0xC8176D90).payload = struct.pack("<dddd", 0, 0, 0, 0)
        return item

    rows.children = []
    for index in range(2):
        row = horizontal.clone("offline_roster_row_" + str(index))
        row.children = [cell("offline_roster_slot_" + str(slot), "OPEN SLOT")
                        for slot in range(4 * index, 4 * index + 4)]
        rows.children.append(row)
    footer = horizontal.clone("offline_roster_footer")
    footer.children = [cell("offline_roster_prev", "PREVIOUS"),
                       cell("offline_roster_status", "RACERS"),
                       cell("offline_roster_next", "NEXT")]
    rows.children.append(footer)
    return Document(document.magic, b"offline_lobby_roster", [(key, rows)])


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--gui", required=True, type=Path, help="Installed Data/gui directory")
    parser.add_argument("--output", required=True, type=Path, help="Generated output directory (not the game directory)")
    parser.add_argument("--rules-panel", action="store_true", help="Add experimental native rules panel; requires runtime layout verification")
    parser.add_argument("--keyboard-only", action="store_true", help="Generate standalone offline_lobby_ui.bgin; do not modify multiplayer layouts")
    parser.add_argument("--panel-only", action="store_true", help="Generate standalone offline_lobby_panel.bgin with empty vertical rows")
    parser.add_argument("--roster-only", action="store_true", help="Generate the native waiting-room roster")
    args = parser.parse_args()
    if sum((args.keyboard_only, args.panel_only, args.roster_only)) > 1:
        parser.error("Choose only one standalone resource")
    source = args.gui / "multiplayer_menu.bgin"
    output = args.output / ("offline_lobby_roster.bgin" if args.roster_only else
                            "offline_lobby_panel.bgin" if args.panel_only else
                            "offline_lobby_ui.bgin" if args.keyboard_only else source.name)
    if output.resolve() == source.resolve() or args.output.resolve() == args.gui.resolve():
        parser.error("Output must be separate from installed assets")
    original = source.read_bytes()
    document = Document.parse(original)
    if document.encode() != original:
        raise ValueError("Lossless source roundtrip failed")
    if args.roster_only:
        controls = Document.parse((args.gui / "windows_contents.bgin").read_bytes())
        document = build_roster_document(document, controls)
    elif args.panel_only:
        document = build_panel_document(document)
    elif args.keyboard_only:
        controls = Document.parse((args.gui / "windows_contents.bgin").read_bytes())
        document = build_keyboard_document(document, controls)
    else:
        add_entry_card(document)
    if args.rules_panel and not args.keyboard_only and not args.panel_only:
        controls = Document.parse((args.gui / "windows_contents.bgin").read_bytes())
        add_rule_checkboxes(document, controls)
    result = document.encode()
    if Document.parse(result).encode() != result:
        raise ValueError("Generated resource roundtrip failed")
    args.output.mkdir(parents=True, exist_ok=True)
    output.write_bytes(result)
    (args.output / "native-ui-build.json").write_text(json.dumps({
        "source": str(source), "generated": str(output),
        "originalBytes": len(original), "generatedBytes": len(result),
        "entryButton": None if args.keyboard_only or args.panel_only else "offline_room_settings_button",
        "rulesPanel": bool(args.rules_panel),
        "keyboard": bool(args.keyboard_only),
        "panel": bool(args.panel_only),
        "panelFactoryPath": "offline_lobby_panel/screen_multiplayer_private_local" if args.panel_only else None,
        "keyboardFactoryPath": "offline_lobby_ui/screen_multiplayer_private_local" if args.keyboard_only else None,
        "runtimeCallbacksBound": False, "runtimeLayoutVerified": False,
        "warning": "Generated from installed copyrighted assets. Do not commit or distribute this output."
    }, indent=2), encoding="utf-8")
    print("Generated local native resource:", output)
    print("Runtime callback binding and visual validation remain required.")


if __name__ == "__main__":
    main()
