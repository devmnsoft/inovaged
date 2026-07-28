import importlib.util
import tempfile
import unittest
from pathlib import Path


SCRIPT = Path(__file__).parents[1] / "assert-trx-has-tests.py"
SPEC = importlib.util.spec_from_file_location("assert_trx", SCRIPT)
MODULE = importlib.util.module_from_spec(SPEC)
assert SPEC.loader is not None
SPEC.loader.exec_module(MODULE)


class TrxCountersTests(unittest.TestCase):
    def test_reads_namespaced_counters(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "result.trx"
            path.write_text(
                '<TestRun xmlns="urn:test"><ResultSummary><Counters total="3" '
                'executed="2" passed="2" failed="0" notExecuted="1" />'
                "</ResultSummary></TestRun>",
                encoding="utf-8",
            )
            self.assertEqual(
                MODULE.counters(path),
                {"total": 3, "executed": 2, "passed": 2, "failed": 0, "skipped": 1},
            )

    def test_rejects_missing_counters(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "result.trx"
            path.write_text("<TestRun />", encoding="utf-8")
            with self.assertRaisesRegex(ValueError, "no Counters"):
                MODULE.counters(path)


if __name__ == "__main__":
    unittest.main()
