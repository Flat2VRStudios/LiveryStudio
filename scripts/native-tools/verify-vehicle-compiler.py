"""Fail on missing compiler output or loss of representative source geometry.

This checks native file readability and mesh/section contracts, not rendered
materials, physics simulation, a game install, or retail compatibility.
"""
import argparse
import collections
import hashlib
import json
from pathlib import Path
import re


def key(path):
    return path.replace('\\', '/').upper().removeprefix('ASSETS:/')


def verify(probe, study):
    fixtures = {key('GRAPH/' + f['p3d']): f for f in study['representatives']}
    inputs = {key(i['archiveMember']): i['sha256'] for i in probe['inputs']}
    results = []
    errors = []
    seen = set()
    for case in probe['cases']:
        asset = key(case['asset'])
        failures = []
        if asset in seen:
            failures.append('Duplicate compiler case')
        seen.add(asset)
        if case.get('exitCode') != 0 or case.get('timedOut'):
            failures.append('Compiler did not exit successfully')
        logs = case.get('stdout', '') + '\n' + case.get('stderr', '')
        if re.search(r'^\s*ERROR:', logs, re.MULTILINE):
            failures.append('Compiler reported an ERROR diagnostic')
        expected = asset[:-4]
        outputs = [(p, m) for p, m in case.get('outputs', {}).items() if key(p) == expected]
        if len(outputs) != 1 or outputs[0][1].get('bytes', 0) <= 0:
            failures.append('Own expected output is absent or empty')
        elif not re.fullmatch('[0-9A-F]{64}', outputs[0][1].get('sha256', '')):
            failures.append('Output hash is missing')
        result = {'asset': case['asset'], 'warningCount': len(re.findall(r'^\s*WARN:', logs, re.MULTILINE))}
        if outputs:
            result['output'] = {'path': outputs[0][0], **outputs[0][1]}
        if asset.endswith('.P3D.XML'):
            fixture = fixtures.get(asset)
            if fixture is None:
                failures.append('No independent source fixture')
            else:
                for source, digest in [(fixture['source'], fixture['sha256']),
                                       (fixture['p3d'], fixture['p3dSha256']),
                                       (fixture['psi'], fixture['psiSha256'])]:
                    if inputs.get(key(source)) != digest:
                        failures.append('Probe/study input hash differs: ' + source)
                native = case.get('nativeRead', {})
                if native.get('exitCode') != 0 or native.get('timedOut'):
                    failures.append('Native geometry reader failed')
                else:
                    try:
                        scene = json.loads(native['stdout'])
                        if scene.get('resourceLoaded') is not True:
                            failures.append('Native resource not loaded')
                        actual = [n for n in scene['nodes'] if n.get('mesh')]
                        if not actual or scene['meshCount'] != len(actual):
                            failures.append('Native mesh inventory is empty/inconsistent')
                        counts = collections.Counter(n['name'] for n in actual)
                        if any(v != 1 for v in counts.values()):
                            failures.append('Duplicate compiled mesh names')
                        discarded = {n['name'] for n in fixture['discard']}
                        source_meshes = {n['name']: n['triangles'] for n in fixture['nodes']
                                         if n['triangles'] > 0 and n['name'] not in discarded
                                         and not re.search(r'_LOD\d+$', n['name'], re.IGNORECASE)}
                        actual_meshes = {n['name']: n['faces'] for n in actual}
                        if source_meshes != actual_meshes:
                            failures.append('Compiled base mesh names/triangle counts differ from independent FBX reader')
                        if scene['baseLodFaces'] != sum(actual_meshes.values()):
                            failures.append('Native aggregate triangle count differs')
                        # LOD bindings are folded into the base compiled mesh.
                        by_name = {n['name']: n for n in actual}
                        for binding in fixture['bindings']:
                            if binding['mesh'] in discarded or re.search(r'_LOD\d+$', binding['mesh'], re.IGNORECASE):
                                continue
                            node = by_name.get(binding['mesh'])
                            if node is None or not 0 <= int(binding['geometry']) < node['sections']:
                                failures.append('Scene binding references an absent mesh/section: ' + binding['mesh'])
                        result.update(nativeNodes=scene['nodeCount'], nativeMeshes=scene['meshCount'],
                                      baseLodFaces=scene['baseLodFaces'], sourceGeometryMatches=source_meshes == actual_meshes)
                    except (KeyError, ValueError, TypeError) as error:
                        failures.append('Invalid native reader result: ' + str(error))
        result['passed'] = not failures
        result['failures'] = failures
        results.append(result)
        errors.extend(case['asset'] + ': ' + error for error in failures)
    expected_cases = set(fixtures) | {key('GRAPH/' + f['psi']) for f in study['representatives']}
    if seen != expected_cases:
        errors.append('Case coverage differs: missing=' + str(sorted(expected_cases - seen)) + ', extra=' + str(sorted(seen - expected_cases)))
    return {'scope': 'Compiler outputs and native P3D hierarchy only; PSI compile is not PSI load proof',
            'passed': not errors, 'compileCases': len(results),
            'nativeGeometryCases': sum('nativeMeshes' in r for r in results),
            'cases': results, 'failures': errors}


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--probe', type=Path, required=True)
    parser.add_argument('--study', type=Path, default=Path(__file__).with_name('mesh-import-study.json'))
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    result = verify(json.loads(args.probe.read_text()), json.loads(args.study.read_text()))
    result['probeSha256'] = hashlib.sha256(args.probe.read_bytes()).hexdigest().upper()
    result['studySha256'] = hashlib.sha256(args.study.read_bytes()).hexdigest().upper()
    args.output.write_text(json.dumps(result, indent=2), encoding='utf-8')
    print(json.dumps({k: v for k, v in result.items() if k != 'cases'}, indent=2))
    raise SystemExit(0 if result['passed'] else 1)


if __name__ == '__main__':
    main()
