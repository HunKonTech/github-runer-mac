from __future__ import annotations

import pathlib


WORKFLOW_PATH = pathlib.Path(__file__).resolve().parents[1] / ".github" / "workflows" / "release.yml"


def test_macos_self_hosted_runner_detection_uses_labels():
    text = WORKFLOW_PATH.read_text()

    assert 'orgs/${OWNER}/actions/runners?per_page=100' in text
    assert 'index("self-hosted")' in text
    assert 'index("macOS")' in text
    assert 'index("ARM64")' in text
    assert '.name == "Koncsik-MacBook-Air"' not in text
