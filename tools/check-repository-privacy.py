#!/usr/bin/env python3
"""Reject private files and recognizable secrets without printing secret values.

Uses only Python's standard library. Local hooks scan the index / outgoing history;
CI scans full reachable history, including deleted files. Deliberately has no
blanket exemption for tests, fixtures, or binary files. Test and benchmark C#
sources can explicitly mark individual synthetic connection-string credentials.
"""
import argparse
import json
import pathlib
import re
import subprocess
import sys


def git(*args):
    return subprocess.check_output(["git", *args], stderr=subprocess.PIPE)


def forbidden_path(path):
    path = path.replace("\\", "/").lower()
    parts = path.split("/")
    name = parts[-1]
    if any(part in {"artifacts", "test-results", "playwright-report"} for part in parts[:-1]):
        return "generated/private runtime directory"
    if path.startswith("src/journey.server/data/"):
        return "private import storage"
    if name.startswith(".env") and (name == ".env" or name.startswith(".env.")):
        if name not in {".env.example", ".env.sample", ".env.template"}:
            return "environment secrets file"
    if re.search(r"\.(?:db|sqlite3?)(?:[-.][^/]*)?$", name):
        return "database or database journal"
    if re.search(r"\.(?:pem|key|p12|pfx|dump|eml|bundle)$", name):
        return "credential, dump, or private mail file"
    if name == "secrets.json" or name.endswith(".secrets.json") or re.fullmatch(r"appsettings(?:\.[^.]+)*\.local\.json", name):
        return "local secret configuration"
    return None


PATTERNS = [
    ("private key", re.compile(r"-----BEGIN (?:RSA |EC |OPENSSH |DSA |ENCRYPTED )?PRIVATE KEY-----")),
    ("GitHub token", re.compile(r"\b(?:gh[pousr]_[A-Za-z0-9]{20,}|github_pat_[A-Za-z0-9_]{30,})\b")),
    ("provider API token", re.compile(r"(?<![A-Za-z0-9_-])(?:sk-(?:proj-|ant-)?[A-Za-z0-9_-]{24,}|AKIA[A-Z0-9]{16}|xox[baprs]-[A-Za-z0-9-]{16,}|AIza[A-Za-z0-9_-]{30,})(?![A-Za-z0-9_-])")),
    ("JWT credential", re.compile(r"\beyJ[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{16,}\b")),
]
CONNECTION = re.compile(r"(?i)\b(?:password|pwd|accountkey|sharedaccesssignature)=(?!=)([^;\s\"'\\]+)")
JSON_SECRET_FIELDS = {"clientsecret", "apikey", "accesskey", "secretkey", "accesstoken", "refreshtoken", "privatekey", "accountkey", "sharedaccesssignature"}
SYNTHETIC_CREDENTIAL = re.compile(r"// privacy: synthetic-credential=(\S+)\s*$")


def synthetic_credential_source(path):
    return bool(re.fullmatch(r"(?:tests/.+|tools/Journey\.[^/]*Bench/.+)\.cs", path.replace("\\", "/")))


def placeholder(value):
    return not value or value.startswith(("${", "$ENV", "<")) or bool(re.fullmatch(r"\$(?:env:)?[A-Za-z_]\w*", value)) or value.lower() in {"changeme", "replace-me", "your-api-key", "your-secret", "placeholder"}


def content_findings(data, path=""):
    if data.startswith(b"SQLite format 3\x00"):
        return {"SQLite content (regardless of filename)"}
    text = data.decode("utf-8", errors="replace")
    texts = [text]
    findings = set()
    allow_synthetic = synthetic_credential_source(path)

    def walk(value):
        if isinstance(value, str):
            texts.append(value)
        elif isinstance(value, dict):
            for key, item in value.items():
                field = re.sub(r"[_:-]", "", key).lower()
                if field in JSON_SECRET_FIELDS and isinstance(item, str) and not placeholder(item):
                    findings.add("non-placeholder secret configuration field")
                walk(item)
        elif isinstance(value, list):
            for item in value:
                walk(item)

    try:
        walk(json.loads(text))
    except (ValueError, RecursionError):
        pass
    for value in texts:
        for name, pattern in PATTERNS:
            if pattern.search(value):
                findings.add(name)
        for line in value.splitlines():
            synthetic = SYNTHETIC_CREDENTIAL.search(line) if allow_synthetic else None
            for match in CONNECTION.finditer(line):
                credential = match.group(1)
                if not placeholder(credential) and not (synthetic and credential == synthetic.group(1)):
                    findings.add("literal connection-string credential")
    return findings


def staged_entries():
    for record in git("ls-files", "--stage", "-z").split(b"\0"):
        if record:
            meta, path = record.split(b"\t", 1)
            mode, oid, stage = meta.decode().split()
            if stage != "0":
                raise RuntimeError("Resolve index conflicts before checking privacy.")
            if mode != "160000":
                yield oid, path.decode("utf-8", errors="surrogateescape")


def history_entries(refs):
    seen = set()
    for commit in git("rev-list", *refs).decode().splitlines():
        for record in git("ls-tree", "-r", "-z", commit).split(b"\0"):
            if not record:
                continue
            meta, path = record.split(b"\t", 1)
            mode, kind, oid = meta.decode().split()
            if kind != "blob":
                continue
            entry = (oid, path.decode("utf-8", errors="surrogateescape"))
            if entry not in seen:
                seen.add(entry)
                yield entry


def scan(entries):
    failures = set()
    checked = 0
    cache = {}
    with subprocess.Popen(["git", "cat-file", "--batch"], stdin=subprocess.PIPE, stdout=subprocess.PIPE) as reader:
        try:
            for oid, path in entries:
                checked += 1
                reason = forbidden_path(path)
                if reason:
                    failures.add((path, reason))
                # The same blob can be allowed in a test but rejected in production.
                cache_key = (oid, synthetic_credential_source(path))
                if cache_key not in cache:
                    reader.stdin.write((oid + "\n").encode())
                    reader.stdin.flush()
                    header = reader.stdout.readline().split()
                    if len(header) != 3 or header[1] != b"blob":
                        raise RuntimeError("Could not read a Git blob.")
                    data = reader.stdout.read(int(header[2]))
                    reader.stdout.read(1)
                    cache[cache_key] = content_findings(data, path)
                for finding in cache[cache_key]:
                    failures.add((path, finding))
        finally:
            reader.stdin.close()
            reader.wait()
    for path, reason in sorted(failures):
        # JSON quoting prevents terminal control characters in filenames.
        print(f"BLOCKED {json.dumps(path)}: {reason}", file=sys.stderr)
    print(f"Privacy check: {checked} file versions; {len(failures)} findings.")
    return 1 if failures else 0


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    mode = parser.add_mutually_exclusive_group(required=True)
    mode.add_argument("--staged", action="store_true")
    mode.add_argument("--history", nargs="+", metavar="REF")
    mode.add_argument("--pre-push", action="store_true")
    args = parser.parse_args()
    try:
        if args.staged:
            return scan(staged_entries())
        refs = args.history
        if args.pre_push:
            refs = []
            for line in sys.stdin:
                fields = line.split()
                if len(fields) != 4:
                    raise RuntimeError("Unexpected pre-push input.")
                oid = fields[1]
                if not re.fullmatch(r"[0-9a-f]{40,64}", oid):
                    raise RuntimeError("Invalid push object ID.")
                if set(oid) != {"0"}:
                    refs.append(oid)
            if not refs:
                return 0
        return scan(history_entries(refs))
    except (subprocess.CalledProcessError, OSError, RuntimeError) as error:
        print(f"Privacy check could not complete ({type(error).__name__}); refusing the operation.", file=sys.stderr)
        return 2


if __name__ == "__main__":
    sys.exit(main())
