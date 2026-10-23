"""Build-only packing of original normal/dirt/damage maps and HDR track probes.

The published app reads the prepared PNG/DDS files directly; Python and Pillow
are not runtime dependencies. Source references and hashes document provenance.
"""
import argparse, hashlib, json, pathlib, re, shutil, xml.etree.ElementTree as ET
from PIL import Image

studio = pathlib.Path(__file__).resolve().parent
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--game-source', type=pathlib.Path, default=studio.parent)
assets = parser.parse_args().game_source.resolve() / 'Flatout/Data/Assets'
output = studio / 'artifacts/preview-assets'
output.mkdir(parents=True, exist_ok=True)
catalog = json.loads((studio / 'Core/Templates/catalog.json').read_text())
records = []

def resolve(value):
    return assets / re.sub(r'^\w+:/', '', value).replace('\\', '/')

def pack_texture(car, name, target):
    folder = assets / 'GRAPH/CARS' / car['Class'] / car['Car'] / 'TEXTURES'
    definition = folder / (name+'.PTX.XML')
    tree = ET.parse(definition)
    source = resolve(tree.find('.//texture').attrib['source'])
    destination = output / f"{car['Index']:02}" / (target+'.png')
    destination.parent.mkdir(parents=True, exist_ok=True)
    image = Image.open(source).convert('RGBA')
    # Preserve linear data channels; color/gamma processing belongs to the renderer.
    if max(image.size) > 1024:
        image.thumbnail((1024, 1024), Image.Resampling.LANCZOS)
    image.save(destination)
    records.append({'Path':str(destination.relative_to(output)).replace('\\','/'),
                    'Source':str(source.relative_to(assets)).replace('\\','/'),
                    'SourceSHA256':hashlib.sha256(source.read_bytes()).hexdigest().upper(),
                    'SHA256':hashlib.sha256(destination.read_bytes()).hexdigest().upper()})

for car in catalog:
    for source, target in [('BODY_NRM','normal'),('BODY_DAMAGE_DIFF','damage'),
                           ('BODY_DAMAGE_NRM','damage-normal'),('BODY_DIRT_IOR','dirt-surface')]:
        pack_texture(car,source,target)
    for kind in ['DUST','MUD','SAND','SNOW']:
        pack_texture(car,'BODY_DIRT_DIFF_'+kind,'dirt-'+kind.lower())
    print(f"Packed car {car['Index']:02}: normal, damage and four original dirt maps")

config = (assets / 'Editor/EnvProbes.cfg').read_text()
environments = []
seen = set()
for block in re.findall(r'\{([^{}]*ProbeName\s*=[\s\S]*?)\n\s*\}', config):
    def field(name):
        match = re.search(name+r'\s*=\s*"([^"]+)"', block)
        return match.group(1) if match else ''
    name = field('ProbeName'); spec = field('CubeMapPath'); irr = field('IrradianceMapPath')
    family = re.sub(r'\s*-\s*\d+.*','',name)
    if not spec or not irr or family in seen: continue
    source = resolve(spec).with_suffix('.dds'); diffuse = resolve(irr).with_suffix('.dds')
    if not source.exists() or not diffuse.exists(): continue
    key = f"probe-{len(environments):02}"
    for original, suffix in [(source,''),(diffuse,'-irr')]:
        destination = output / (key+suffix+'.dds');shutil.copyfile(original,destination)
        records.append({'Path':destination.name,'Source':str(original.relative_to(assets)).replace('\\','/'),
                        'SourceSHA256':hashlib.sha256(original.read_bytes()).hexdigest().upper(),
                        'SHA256':hashlib.sha256(destination.read_bytes()).hexdigest().upper()})
    pos = re.search(r'SunPos\s*=\s*\{\s*([-\d.]+)',block)
    intensity = re.search(r'SunIntensity\s*=\s*([\d.]+)',block)
    environments.append({'Name':name,'Specular':key+'.dds','Irradiance':key+'-irr.dds',
                         'LightAngle':float(pos.group(1))%360 if pos else 120,
                         'SunStrength':float(intensity.group(1)) if intensity else 1})
    seen.add(family)
    if len(environments)>=10:break
if not environments:raise RuntimeError('No original HDR environment probes were prepared')
(output/'environments.json').write_text(json.dumps(environments,indent=2))
(output/'asset-manifest.json').write_text(json.dumps(records,indent=2))
print(f"Prepared {len(records)} files and {len(environments)} real track environments")
