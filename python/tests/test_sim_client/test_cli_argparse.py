"""Argparse-level smoke tests for the rusim CLI.

These tests live INDEPENDENTLY of any handler logic — they only exercise
``build_parser()``. The point is to lock in the wire shape of the CLI
(every top-level command and every nested subcommand parses ``--help``
without crashing) so the planned ``cli.py`` → ``cli/`` package split can
land safely. A future commit that forgets to wire a subparser, or moves
a helper but leaves a dangling import, will fail one of these tests.

If you add a new subcommand to ``sim_client/cli.py``, also add it here.
"""

from __future__ import annotations

import pytest

from sim_client.cli import build_parser, main


# Top-level subcommands currently registered by build_parser().
TOP_LEVEL_SUBCOMMANDS = [
    "doctor",
    "version",
    "contract",
    "install",
    "upgrade",
    "list",
    "inspect",
    "reset",
    "step",
    "model",
    "runtime",
    "server",
    "scenario",
    "plugin",
]

# Nested subcommands: (group, [sub1, sub2, ...]).
NESTED_SUBCOMMANDS = [
    ("model", ["install", "list", "catalog", "activate", "active", "binding", "bind"]),
    (
        "runtime",
        [
            "build",
            "list",
            "inspect",
            "run",
            "favorite",
            "remove",
            "upgrade",
        ],
    ),
    ("server", ["up", "start", "status", "down", "stop"]),
    ("scenario", ["validate", "reset", "print-reset", "list"]),
    ("plugin", ["install", "list", "remove", "new"]),
]


# ---------------------------------------------------------------------------
# Top-level
# ---------------------------------------------------------------------------


def test_build_parser_does_not_raise() -> None:
    """The argparse construction itself must not crash on import."""
    parser = build_parser()
    assert parser is not None


def test_main_help_returns_zero(capsys: pytest.CaptureFixture[str]) -> None:
    """`rusim --help` must succeed and print usage."""
    with pytest.raises(SystemExit) as excinfo:
        main(["--help"])
    assert excinfo.value.code == 0
    captured = capsys.readouterr()
    assert "usage" in captured.out.lower()


@pytest.mark.parametrize("name", TOP_LEVEL_SUBCOMMANDS)
def test_top_level_subcommand_help(name: str, capsys: pytest.CaptureFixture[str]) -> None:
    """`rusim <name> --help` must succeed for every registered top-level command."""
    parser = build_parser()
    with pytest.raises(SystemExit) as excinfo:
        parser.parse_args([name, "--help"])
    assert excinfo.value.code == 0
    captured = capsys.readouterr()
    assert "usage" in captured.out.lower()


# ---------------------------------------------------------------------------
# Nested
# ---------------------------------------------------------------------------


def _expand_nested() -> list[tuple[str, str]]:
    out = []
    for group, subs in NESTED_SUBCOMMANDS:
        for s in subs:
            out.append((group, s))
    return out


@pytest.mark.parametrize(("group", "sub"), _expand_nested())
def test_nested_subcommand_help(group: str, sub: str, capsys: pytest.CaptureFixture[str]) -> None:
    """`rusim <group> <sub> --help` must succeed for every nested subcommand."""
    parser = build_parser()
    with pytest.raises(SystemExit) as excinfo:
        parser.parse_args([group, sub, "--help"])
    assert excinfo.value.code == 0
    captured = capsys.readouterr()
    assert "usage" in captured.out.lower()


# ---------------------------------------------------------------------------
# Drift detection — guards against silent removal/addition
# ---------------------------------------------------------------------------


def test_top_level_command_set_matches_expected() -> None:
    """If a new top-level subcommand is added (or one is removed) without
    updating this test's TOP_LEVEL_SUBCOMMANDS list, fail loudly so the
    drift can't slip in. Counts against the actual subparser registry."""
    parser = build_parser()
    # argparse stores subparsers in `_actions[*].choices` for the SubParsersAction.
    sub_actions = [a for a in parser._actions if hasattr(a, "choices") and isinstance(a.choices, dict)]
    assert sub_actions, "build_parser() did not register any subparsers"
    actual = set(sub_actions[0].choices.keys())
    expected = set(TOP_LEVEL_SUBCOMMANDS)
    missing_in_test = actual - expected
    extra_in_test = expected - actual
    assert not missing_in_test, (
        f"build_parser() registers commands not listed in TOP_LEVEL_SUBCOMMANDS: {missing_in_test}. "
        f"Add them to the test fixture."
    )
    assert not extra_in_test, (
        f"TOP_LEVEL_SUBCOMMANDS lists commands not in build_parser(): {extra_in_test}. "
        f"Remove them from the test fixture or add them to build_parser()."
    )
