"""Observe DLL loads while rebuilding the isolated Buick geometry.

Uses the developer's Windows debugger, not a dependency of the shipping worker.
Does not remove installed runtimes or claim a clean-machine/retail test.
"""
import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest().upper()


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--probe', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--debugger', type=Path, default=Path(os.environ['ProgramFiles(x86)']) /
                        'Windows Kits/10/Debuggers/x64/cdb.exe')
    args = parser.parse_args()
    probe = json.loads(args.probe.read_text())
    workspace = Path(probe['workspace']).resolve()
    runtime = workspace / 'Runtime'
    manifest = json.loads((runtime / 'manifest.json').read_text())
    errors = []
    for relative, metadata in manifest['files'].items():
        path = runtime / relative
        if not path.is_file() or sha(path) != metadata['sha256']:
            errors.append('Staged runtime mismatch: ' + relative)
    asset = 'ASSETS:/GRAPH/Cars/DERBY/Buick/Buick.P3D.XML'
    case = next(c for c in probe['cases'] if c['asset'].upper() == asset.upper())
    compiled = case.get('compiledOutput')
    if not compiled:
        raise ValueError('Probe must record its own compiled output')
    output = workspace / 'Project/Data/WIN32' / compiled['path']
    if not output.is_file() or sha(output) != compiled['sha256']:
        errors.append('Compiled input to the repeat differs from probe')
    log = workspace / 'runtime-module-loads.log'
    command = [str(args.debugger.resolve()), '-hd', '-logo', str(log), '-G',
               '-c', 'lmf;sxe av;g;.ecxr;kv;q', str(runtime / 'Bin/Compiler_r.exe'),
               '/Input', asset, '/Platform', 'PC', '/ProjectPath', str(workspace / 'Project')]
    result = subprocess.run(command, cwd=runtime / 'Bin', capture_output=True, timeout=45,
                            creationflags=getattr(subprocess, 'CREATE_NO_WINDOW', 0))
    if result.returncode != 0:
        errors.append('Debugger/compiler exited with code ' + str(result.returncode))
    text = log.read_text(errors='replace')
    if re.search(r'^\s*ERROR:', text, re.MULTILINE):
        errors.append('Repeat compilation reported an error')
    modules = sorted(set(re.findall(r'^ModLoad:\s+[0-9a-f`]+\s+[0-9a-f`]+\s+(.+)$', text,
                                    flags=re.IGNORECASE | re.MULTILINE)))
    # CDB abbreviates the two startup ModLoad paths; lmf supplies their paths.
    startup = {}
    for path in re.findall(r'^\s*[0-9a-f`]+\s+[0-9a-f`]+\s+\S+\s+([A-Z]:\\[^\r\n]+)$', text,
                           flags=re.IGNORECASE | re.MULTILINE):
        startup[Path(path.strip()).name.lower()] = Path(path.strip())
    # Some CDB versions retain short names even in lmf for the initial image
    # and KnownDLL ntdll. Record that qualification instead of treating them
    # as files in this Python process's working directory.
    startup.setdefault('compiler_r.exe', runtime / 'Bin/Compiler_r.exe')
    startup.setdefault('ntdll.dll', Path(os.environ['WINDIR']) / 'System32/ntdll.dll')
    unqualified = [path.strip() for path in modules if not Path(path.strip()).is_absolute()]
    if any(name.lower() not in startup for name in unqualified):
        errors.append('Unqualified module paths: ' + str(unqualified))
    modules = [startup.get(Path(path.strip()).name.lower(), Path(path.strip())).resolve() for path in modules]
    required = {'compiler_r.exe', 'importer64_r.dll', 'writer64_r.dll', 'writerphysx_r.dll',
                'enginedll64_r.dll', 'globals64_r.dll', 'tootlesoftwareonly_2k8_64.dll',
                'msvcp90.dll', 'msvcr90.dll', 'msvcp140.dll', 'vcruntime140.dll',
                'vcruntime140_1.dll', 'physxcooking64_r.dll'}
    local = {path.name.lower() for path in modules if path.is_relative_to(runtime)}
    if required - local:
        errors.append('Required module not observed in bundle: ' + str(sorted(required - local)))
    windows = Path(os.environ['WINDIR']).resolve()
    # This PC's audio driver injects these independently of the worker's import
    # graph. Record them explicitly; never copy them into the redistributable.
    audio_hook = Path(os.environ['ProgramData']) / 'A-Volute/DellInc.AlienwareSoundCenter/Modules/ScheduledModules/x64'
    host_audio = [str(path) for path in modules if path.is_relative_to(audio_hook)]
    external = [str(path) for path in modules if not path.is_relative_to(windows)
                and not path.is_relative_to(runtime) and not path.is_relative_to(audio_hook)]
    if external:
        errors.append('Modules loaded outside Windows and the worker: ' + str(external))
    if not output.is_file() or sha(output) != compiled['sha256']:
        errors.append('Repeat compilation changed the geometry bytes')
    report = {'scope': 'Developer-PC module-load/repeat audit only; no clean-PC or game validation',
              'passed': not errors, 'probeSha256': sha(args.probe),
              'debugger': str(args.debugger.resolve()), 'debuggerExitCode': result.returncode,
              'log': str(log), 'logSha256': sha(log),
              'bundledModules': [{'path': path.relative_to(runtime).as_posix(), 'sha256': sha(path)}
                                 for path in modules if path.is_relative_to(runtime)],
              'windowsModules': [str(path) for path in modules if path.is_relative_to(windows)],
              'externalModules': external, 'hostInjectedAudioModules': host_audio,
              'startupNamesQualifiedFromCommandAndWindows': unqualified,
              'requiredBundledModulesObserved': sorted(required & local),
              'repeatOutput': {'sha256': sha(output), 'matchesProbe': sha(output) == compiled['sha256']},
              'cleanPcValidated': False, 'failures': errors}
    args.output.write_text(json.dumps(report, indent=2), encoding='utf-8')
    print(json.dumps({'passed': report['passed'], 'bundledModules': len(report['bundledModules']),
                      'repeatMatches': report['repeatOutput']['matchesProbe'], 'failures': errors}, indent=2))
    raise SystemExit(0 if report['passed'] else 1)


if __name__ == '__main__':
    main()
