"""Audit a working source tree or exported snapshot without printing secrets."""
import argparse
import json
from pathlib import Path
import re
import subprocess

FORBIDDEN = {'reference', 'verification', 'integration', 'tutorial', '.git', 'bin', 'obj', 'artifacts'}
PATTERNS = {
    'private key': re.compile(rb'-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----'),
    'GitHub token': re.compile(rb'\b(?:gh[pousr]_[A-Za-z0-9]{30,}|github_pat_[A-Za-z0-9_]{50,})\b'),
    'AWS access key': re.compile(rb'\b(?:AKIA|ASIA)[A-Z0-9]{16}\b'),
    'API credential': re.compile(rb'\bsk-(?:proj-|svcacct-)?[A-Za-z0-9_-]{24,}\b'),
}

def paths(root):
    if (root / '.git').exists():
        names = subprocess.run(['git', '-C', str(root), 'ls-files', '-z', '--cached', '--others', '--exclude-standard'], check=True, capture_output=True).stdout.decode().split('\0')
        return sorted({root / n for n in names if n and (root / n).is_file()})
    return sorted(p for p in root.rglob('*') if p.is_file())

def audit(root):
    root = Path(root).resolve()
    failures = []
    files = paths(root)
    for path in files:
        relative = path.relative_to(root)
        if any(p in FORBIDDEN or p.startswith('publish') for p in relative.parts) or path.suffix.lower() in ('.log','.pdb','.ilk','.rdc'):
            failures.append({'file': relative.as_posix(), 'reason':'private/generated development material'})
        if path.is_symlink():
            failures.append({'file': relative.as_posix(), 'reason':'source link'})
        data = path.read_bytes()
        for label, pattern in PATTERNS.items():
            if pattern.search(data):
                failures.append({'file': relative.as_posix(), 'reason':label})
        if path.suffix.lower() in ('.cs','.xaml','.py','.ps1','.md','.json','.vcxproj') and 'third_party' not in relative.parts:
            if re.search(rb'[A-Z]:[/\\](?:Users|Github|RQ-Engine)[/\\]', data, re.I):
                failures.append({'file': relative.as_posix(), 'reason':'machine-specific path'})
        if path.suffix.lower() == '.md':
            text = data.decode('utf-8-sig')
            for target in re.findall(r'\]\(([^)]+)\)', text):
                if re.match(r'^(?:https?:|mailto:|#)', target):
                    continue
                target = target.split('#')[0]
                if target and not (path.parent / target).exists():
                    failures.append({'file': relative.as_posix(), 'reason':'broken link: '+target})
    for name in ('LICENSE','README.md','THIRD_PARTY_NOTICES.md','SECURITY.md','CHANGELOG.md'):
        if not (root / name).is_file():
            failures.append({'file':name,'reason':'required public document missing'})
    return {'FilesChecked':len(files), 'Passed':not failures, 'Findings':failures}

if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--root', type=Path, default=Path(__file__).resolve().parents[1])
    parser.add_argument('--record', type=Path)
    args = parser.parse_args()
    result = audit(args.root)
    if args.record:
        args.record.parent.mkdir(parents=True,exist_ok=True)
        args.record.write_text(json.dumps(result,indent=2)+'\n')
    print(json.dumps(result,indent=2))
    raise SystemExit(0 if result['Passed'] else 1)
