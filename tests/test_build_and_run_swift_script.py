from __future__ import annotations

import pathlib
import re


SCRIPT_PATH = pathlib.Path(__file__).resolve().parents[1] / "scripts" / "build_and_run_swift.sh"


def test_bundle_mode_does_not_stop_running_app():
    text = SCRIPT_PATH.read_text()
    body = re.search(r"should_stop_existing_app\(\) \{\n(?P<body>.*?)\n\}", text, re.S)

    assert body is not None
    bundle_branch = body.group("body").split("--bundle|bundle)", 1)[1].split(";;", 1)[0]
    assert "return 1" in bundle_branch
    assert re.search(
        r'if should_stop_existing_app "\$MODE"; then\s+pkill -x "\$PRODUCT_NAME".*?\nfi',
        text,
        re.S,
    )
