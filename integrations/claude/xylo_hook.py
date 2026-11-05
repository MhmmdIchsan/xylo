"""Forward Claude Code hook events using the shared Xylo bridge."""
import pathlib
import sys

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parents[1] / "codex"))
from xylo_hook import main

try:
    main("Claude Code")
except (ValueError, OSError, TypeError):
    pass
