"""Build-time extraction of the game's actual car meshes and livery UVs.

Only the generated FLSM meshes ship; Python, FBX and ufbx are not runtime dependencies.
"""
import argparse, json, pathlib, re, struct, subprocess, sys, xml.etree.ElementTree as ET

studio = pathlib.Path(__file__).resolve().parent
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--game-source', type=pathlib.Path, default=studio.parent)
repo = parser.parse_args().game_source.resolve()
assets = repo / 'Flatout/Data/Assets'
output = studio / 'artifacts/models'
output.mkdir(parents=True, exist_ok=True)
catalog = json.loads((studio / 'Core/Templates/catalog.json').read_text())
config = (repo / 'Flatout/Data/Common/Tuning/Factories/Cars.cfg').read_text()
converter = studio / 'artifacts/native/MeshConverter.exe'

def resolve(value):
    return assets / re.sub(r'^\w+:/', '', value).replace('\\', '/')

def extract(psi, tag, wheel=False):
    tree = ET.parse(psi)
    scene = resolve(tree.find('.//scene_instance').attrib['scene'])
    fbx = resolve(ET.parse(scene).find('.//p3d').attrib['path'])
    binds = list(tree.findall('.//bind'))
    lines = []
    for bind in binds:
        a = bind.attrib; name = a['mesh']; mat = a.get('material', '').upper()
        if not mat or re.search(r'LOD|BREAK|\$PHYS|ARROW|RTT|MIRROR_SPACE', name, re.I): continue
        if wheel and name.endswith('_C') and any(b.attrib['mesh'] == name[:-1]+'B' for b in binds): continue
        kind = 1 if mat.endswith('/LOAD.PMA.XML') or mat.endswith('\\LOAD.PMA.XML') else 2 if 'GLASS' in mat else 3 if 'TYRE' in mat else 4 if 'OPTICS' in mat else 0
        if wheel: kind = 3
        lines.append(f"{name}\t{a.get('geometry', '0')} {kind}\n")
    mapping = output / (tag+'.bindings')
    mapping.write_text(''.join(lines))
    binary = output / (tag+'.flsm')
    subprocess.run([str(converter), str(fbx), str(mapping), str(binary)], check=True, capture_output=True)
    raw = binary.read_bytes()
    magic, count = struct.unpack_from('<II', raw)
    assert magic == 0x4d534c46 and len(raw) == 8+count*36
    vertices = [list(v) for v in struct.iter_unpack('<9f', raw[8:])]
    mapping.unlink()
    return vertices

records = []
for car in catalog:
    base = car['Car'].replace('_EVO','').replace('_G850','').replace('_V8','')
    engine = car['Class']+'_'+('IceCreamVan' if base=='ICECREAM_VAN' else base.title())
    block = re.search(r'^\t'+re.escape(engine)+r'\s*=\s*\{(.*?)^\t\}', config, re.M|re.S)
    if not block: raise RuntimeError('No car configuration: '+engine)
    fields = block.group(1)
    body_path = re.search(r'BaseModel\s*=\s*"([^"]+)"', fields).group(1)
    body = extract(resolve(body_path.replace('DATA:/','ASSETS:/')+'.XML'), f"{car['Index']:02}")
    body_count = len(body)
    for axle in ['Front','Rear']:
        wheel_path = re.search(axle+r'Wheels\s*=\s*"([^"]+)"', fields).group(1)
        pos = [float(x.strip()) for x in re.search(axle+r'WheelsPos\s*=\s*\{([^}]+)',fields).group(1).split(',')]
        wheel = extract(resolve(wheel_path.replace('DATA:/','ASSETS:/')+'.XML'), f"wheel-{car['Index']:02}-{axle}", True)
        for sign in [-1,1]:
            for v in wheel:
                copy=v.copy();copy[0]=v[0]*sign+pos[0]*sign;copy[1]=v[1]+pos[2];copy[2]=v[2]-pos[1];copy[3]*=sign
                body.append(copy)
        (output/f"wheel-{car['Index']:02}-{axle}.flsm").unlink()
    minimum = [min(v[i] for v in body) for i in range(3)]
    maximum = [max(v[i] for v in body) for i in range(3)]
    center = [(minimum[i]+maximum[i])/2 for i in range(3)]
    for v in body:
        for i in range(3): v[i]-=center[i]
    data=struct.pack('<II',0x4d534c46,len(body))+b''.join(struct.pack('<9f',*v) for v in body)
    (output/f"{car['Index']:02}.flsm").write_bytes(data)
    paint = sum(1 for v in body if v[8]==1)//3
    if paint<100:raise RuntimeError('No livery surface: '+car['Name'])
    records.append({'Index':car['Index'],'Name':car['Name'],'Triangles':len(body)//3,'PaintTriangles':paint,'BodyTriangles':body_count//3,'Bounds':[minimum,maximum]})
    print(f"{car['Index']:02} {car['Name']}: {len(body)//3} triangles, {paint} paintable")
(output/'catalog.json').write_text(json.dumps(records,indent=2))
