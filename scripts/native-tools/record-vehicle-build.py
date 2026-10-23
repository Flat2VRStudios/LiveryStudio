"""Record the actual MSVC read closure and outputs of isolated native builds."""
import argparse
import hashlib
import json
from pathlib import Path
import subprocess


def sha(path):
    with path.open('rb') as file:
        return hashlib.file_digest(file, 'sha256').hexdigest().upper()


def git(game_source, *args):
    return subprocess.check_output(['git', '-C', str(game_source), *args], text=True).strip()


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--game-source', type=Path, required=True)
    parser.add_argument('--build-root', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    game_source = args.game_source.resolve()
    builds = []
    profiles=['compiler','importer','cooker','reader']
    if (args.build_root/'writer').is_dir():profiles.append('writer')
    for profile in profiles:
        root = args.build_root.resolve() / profile
        read_files = set()
        tlogs = list((root / 'obj').rglob('CL.read.*.tlog'))
        if not tlogs:
            raise ValueError('Missing compiler dependency logs: ' + str(root))
        for log in tlogs:
            for line in log.read_text(encoding='utf-16').splitlines():
                if not line:
                    continue
                if line.startswith('^'):
                    for source in line[1:].split('|'):
                        path=Path(source)
                        if path.is_absolute() and path.is_file():read_files.add(path.resolve())
                    continue
                path = Path(line)
                if path.is_absolute() and path.is_file():
                    read_files.add(path.resolve())
        if not read_files:
            raise ValueError('Empty native source/header closure')
        outputs = {path.relative_to(root).as_posix(): sha(path) for path in (root / 'bin').glob('*')
                   if path.suffix.lower() in ('.exe', '.dll')}
        if not outputs:
            raise ValueError('No native executable/library output')
        builds.append({'profile': profile, 'workspace': str(root),
                       'generatedInputs': json.loads((root / 'build-inputs.json').read_text()),
                       'generatedOverlayFiles': {path.relative_to(root).as_posix():sha(path)
                                                 for path in sorted(set(root.glob('*.cpp')) | set(root.glob('*.vcxproj')) |
                                                                    {p for p in (root/'include').rglob('*') if p.is_file()})},
                       'prebuiltLibraries': json.loads((root / 'input-libraries.json').read_text()),
                       'msvcReadClosure': {str(path): sha(path) for path in sorted(read_files)},
                       'outputs': outputs, 'buildLogSha256': sha(root / 'build.log')})
    report = {'scope': 'Actual native build inputs/outputs on this PC; existing game source libraries are inputs',
              'gameSourceRoot': str(game_source), 'gameSourceHead': git(game_source, 'rev-parse', 'HEAD'),
              'gameSourceDirtyStatus': git(game_source, 'status', '--short'), 'builds': builds,
              'recorderSha256': sha(Path(__file__).resolve())}
    args.output.write_text(json.dumps(report, indent=2), encoding='utf-8')
    print(json.dumps({'builds': len(builds), 'readClosureFiles': [len(b['msvcReadClosure']) for b in builds]}, indent=2))


if __name__ == '__main__':
    main()
