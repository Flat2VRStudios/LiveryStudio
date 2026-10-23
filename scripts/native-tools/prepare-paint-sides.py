"""Generate dual-UV shaders from authorized private inputs, without vendoring them."""
import argparse
from pathlib import Path
import subprocess
import xml.etree.ElementTree as ET

BASE_REVISION = 'a882b0a38cf84bce25155db77ade35ddca6c3d02'

def prepare(game_source, output, revision=BASE_REVISION):
    output = Path(output).resolve()
    if output.is_relative_to(Path(game_source).resolve()):
        raise ValueError('Generated shaders must stay outside the game source')
    def source(name):
        return subprocess.run(['git', '-C', str(game_source), 'show',
            revision + ':Flatout/Data/Assets/Effects/' + name],
            check=True, capture_output=True).stdout.decode('utf-8-sig')
    shader = source('CarPaint.cg')
    replacements = {
        'half2 uv = surfaceInput.Uv0 * TileUV.xy;': 'half2 uv = surfaceInput.Uv0 * TileUV.xy;\n\thalf2 paintUv = surfaceInput.Uv1;',
        'h4tex2D( DiffuseMapSampler, uv )': 'h4tex2D( DiffuseMapSampler, paintUv )',
        'h4tex2D( StructureMapSampler, uv )': 'h4tex2D( StructureMapSampler, paintUv )',
        'oInput.Uv01.xy*TileUV.xy': 'oInput.Uv01.zw',
    }
    for old, new in replacements.items():
        if shader.count(old) != 1:
            raise ValueError('The game shader does not match the supported UV contract')
        shader = shader.replace(old, new)
    shader = '#define ENABLE_2_UVS 1\n' + '\n'.join(line.rstrip() for line in shader.splitlines()) + '\n'
    document = ET.fromstring(source('CarPaint.PFX.XML'))
    for element in document.iter():
        if 'path' in element.attrib:
            element.set('path', element.get('path').replace('CarPaint.cg', 'PaintSides.cg'))
        if element.get('nbuvs') == '1':
            element.set('nbuvs', '2')
    ET.SubElement(document.find('.//triggers'), 'trigger', {'id':'15', 'macro':'D_NO_VELOCITY', 'name':'No MotionVector', 'register':'NoMotionVector', 'value':'false'})
    for value in ('No MotionVector', 'Use DiffuseMap;No MotionVector',
                  'Use DiffuseMap;Use NormalMap;No MotionVector',
                  'Use DiffuseMap;Use NormalMap;Use AlphaTest;No MotionVector'):
        ET.SubElement(document.find('.//TriggerCombinations'), 'TriggerCombination', {'value':value})
    output.mkdir(parents=True, exist_ok=False)
    (output / 'PaintSides.cg').write_text(shader, encoding='utf-8', newline='\n')
    ET.ElementTree(document).write(output / 'PaintSides.PFX.XML', encoding='utf-8', xml_declaration=True)

if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--game-source', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--game-ref', default=BASE_REVISION)
    args = parser.parse_args()
    prepare(args.game_source, args.output, args.game_ref)
