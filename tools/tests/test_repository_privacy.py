import importlib.util
import json
import pathlib
import subprocess
import sys
import tempfile
import unittest

SCRIPT = pathlib.Path(__file__).resolve().parents[1] / "check-repository-privacy.py"
SPEC = importlib.util.spec_from_file_location("privacy", SCRIPT)
PRIVACY = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(PRIVACY)


class PrivacyTests(unittest.TestCase):
    def test_runtime_files_are_blocked_anywhere(self):
        for path in ["journey.db", "nested/journey.db-wal", "journey.sqlite3-shm", "journey.db.gz", "backup.bundle", ".env.local", "nested/secrets.json", "src/Journey.Server/data/import.json", "x/test-results/error-context.md", "artifacts/backup.bundle", "key.pem", "mail.eml"]:
            with self.subTest(path=path):
                self.assertIsNotNone(PRIVACY.forbidden_path(path))

    def test_source_and_templates_are_allowed(self):
        for path in ["src/Journey.Server/appsettings.json", "fixtures/conversation.json", ".env.example", "src/AppDb.cs"]:
            self.assertIsNone(PRIVACY.forbidden_path(path))

    def test_renamed_database_is_still_detected(self):
        self.assertTrue(PRIVACY.content_findings(b"SQLite format 3\x00" + b"x" * 50))

    def test_connection_secret_in_json_escaped_chat_is_detected(self):
        credential = "Generated" + "TestValue" + "123!"
        text = "Host=example.database.azure.com;User=test;" + "Password=" + credential + ";"
        data = json.dumps({"text": text}).replace("Password", "Pass\\u0077ord").encode()
        self.assertIn("literal connection-string credential", PRIVACY.content_findings(data))

    def test_templates_and_css_are_not_false_positives(self):
        self.assertFalse(PRIVACY.content_findings(b"Password=${postgresAdminPassword}; --xy-minimap-mask-background-color-default"))
        self.assertFalse(PRIVACY.content_findings(b"Password=$pw;"))

    def test_secrets_are_detected_even_in_template_files(self):
        data = json.dumps({"ClientSecret": "Generated" + "TestValue" + "123!"}).encode()
        self.assertIn("non-placeholder secret configuration field", PRIVACY.content_findings(data))

    def test_synthetic_connection_credentials_require_explicit_scoped_markers(self):
        data = b'var text = "password=' + b'synthetic-example"; // privacy: synthetic-credential=synthetic-example'
        for path in ["tests/Example.cs", "tests/nested/Example.cs", "tools/Journey.ImportBench/Runner.cs", r"tools\Journey.ScaleBench\Runner.cs"]:
            with self.subTest(path=path):
                self.assertFalse(PRIVACY.content_findings(data, path))
        for path in ["", "src/Example.cs", "fixtures/example.json", "tests/example.json", "tools/Journey.Server/Runner.cs"]:
            with self.subTest(path=path):
                self.assertIn("literal connection-string credential", PRIVACY.content_findings(data, path))

    def test_test_files_still_detect_unmarked_and_different_credentials(self):
        for data in [
            b'password=' + b'synthetic-example',
            b'password=' + b'other-value // privacy: synthetic-credential=synthetic-example',
            b'password=' + b'synthetic-example\n// privacy: synthetic-credential=synthetic-example',
            b'password=' + b'synthetic-example;pwd=' + b'other-value // privacy: synthetic-credential=synthetic-example',
        ]:
            with self.subTest(data=data):
                self.assertIn("literal connection-string credential", PRIVACY.content_findings(data, "tests/Example.cs"))

    def test_synthetic_markers_do_not_exempt_tokens_or_private_files(self):
        token = "gh" + "p_" + "aB12" * 10
        data = ('password=' + 'synthetic-example; ' + token + ' // privacy: synthetic-credential=synthetic-example').encode()
        self.assertIn("GitHub token", PRIVACY.content_findings(data, "tests/Example.cs"))
        self.assertIsNotNone(PRIVACY.forbidden_path("tests/secrets.json"))
        self.assertTrue(PRIVACY.content_findings(b"SQLite format 3\x00", "tests/Example.cs"))
        self.assertTrue(PRIVACY.content_findings(json.dumps({"ClientSecret": "synthetic-example"}).encode(), "tests/Example.cs"))

    def test_identical_blobs_are_scanned_in_each_path_context(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = pathlib.Path(tmp)
            subprocess.run(["git", "init", "-q", str(root)], check=True, capture_output=True)
            (root / "tests").mkdir()
            data = 'password=' + 'synthetic-example // privacy: synthetic-credential=synthetic-example'
            # Both production-first and test-first cache orders must reject production.
            for path in ["a-production.cs", "tests/Example.cs", "z-production.cs"]:
                (root / path).write_text(data)
            subprocess.run(["git", "add", "."], cwd=root, check=True, capture_output=True)
            staged = subprocess.run([sys.executable, str(SCRIPT), "--staged"], cwd=root, capture_output=True, text=True)
            self.assertEqual(staged.returncode, 1)
            self.assertIn('BLOCKED "a-production.cs"', staged.stderr)
            self.assertIn('BLOCKED "z-production.cs"', staged.stderr)
            self.assertNotIn('BLOCKED "tests/Example.cs"', staged.stderr)

    def test_scanner_and_regression_sources_are_safe_to_commit(self):
        # Build fake credentials at runtime so these tests do not trip the hook.
        for source in [SCRIPT, pathlib.Path(__file__).resolve()]:
            with self.subTest(source=source.name):
                self.assertFalse(PRIVACY.content_findings(source.read_bytes(), source.relative_to(SCRIPT.parent.parent).as_posix()))

    def test_index_and_deleted_history_are_scanned_not_working_tree(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = pathlib.Path(tmp)
            def git(*args):
                result = subprocess.run(["git", *args], cwd=root, capture_output=True)
                if result.returncode:
                    raise RuntimeError(result.stderr.decode(errors="replace"))
                return result
            git("init", "-q")
            git("config", "user.name", "Privacy Test")
            git("config", "user.email", "privacy@example.test")
            git("config", "commit.gpgsign", "false")
            git("config", "core.hooksPath", str(root / "empty-hooks"))
            credential = "gh" + "p_" + "aB12" * 10
            (root / "notes.txt").write_text(credential)
            git("add", "notes.txt")
            (root / "notes.txt").write_text("safe working copy")
            staged = subprocess.run([sys.executable, str(SCRIPT), "--staged"], cwd=root, capture_output=True, text=True)
            self.assertEqual(staged.returncode, 1)
            self.assertNotIn(credential, staged.stdout + staged.stderr)
            git("commit", "-qm", "Test history")
            git("rm", "-f", "notes.txt")
            git("commit", "-qm", "Delete test file")
            history = subprocess.run([sys.executable, str(SCRIPT), "--history", "HEAD"], cwd=root, capture_output=True, text=True)
            self.assertEqual(history.returncode, 1)
            self.assertNotIn(credential, history.stdout + history.stderr)
            oid = git("rev-parse", "HEAD").stdout.decode().strip()
            push = subprocess.run([sys.executable, str(SCRIPT), "--pre-push"], cwd=root, input=f"refs/heads/main {oid} refs/heads/main {'0' * 40}\n", capture_output=True, text=True)
            self.assertEqual(push.returncode, 1)
            self.assertNotIn(credential, push.stdout + push.stderr)


if __name__ == "__main__":
    unittest.main()
