"""Pytest fixtures and path setup for the uav-simulator Python suite.

The project ships with three Python trees under ``python/``:

* ``sim_client`` — installed package (HTTP client + ``rusim`` CLI),
* ``training``   — RL wrappers and training entry-points,
* ``bridges``    — ROS2 bridges (rclpy-optional).

Only ``sim_client`` is exposed via ``pyproject.toml``; ``training`` and
``bridges`` are left importable from the repo checkout to keep the iterative
research workflow short. ``[tool.pytest.ini_options].pythonpath = ["."]``
already adds ``python/`` to ``sys.path``, but we additionally drop the
historical ``sys.path.insert(0, str(PYTHON_ROOT))`` boilerplate from each
test by ensuring the path is in place before any test module is loaded.
"""

from __future__ import annotations

import sys
from pathlib import Path

PYTHON_ROOT = Path(__file__).resolve().parents[1]

if str(PYTHON_ROOT) not in sys.path:
    sys.path.insert(0, str(PYTHON_ROOT))
