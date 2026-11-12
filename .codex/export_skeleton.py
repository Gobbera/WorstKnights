"""Export the isolated Blender snapshot; never write the live authoring file."""
from pathlib import Path
import bpy
import json
from mathutils import Matrix

project = Path(__file__).resolve().parents[1]
work = json.loads((project / '.codex/skeleton-work-state.json').read_text(encoding='utf-8'))
destination = project / work['asset_directory']
equipment_directory = destination / 'Equipment'
equipment_directory.mkdir(parents=True, exist_ok=True)
scene = bpy.context.scene
rig = bpy.data.objects['Skeleton']
assert rig.location.length < 1e-6
if bpy.context.mode != 'OBJECT':
    bpy.ops.object.mode_set(mode='OBJECT')

def flat(matrix):
    return [float(matrix[r][c]) for r in range(4) for c in range(4)]

def activate(owner, action):
    animation = owner.animation_data_create()
    animation.use_nla = False
    animation.action = action
    animation.action_slot = action.slots[0]

def select(objects):
    for obj in bpy.context.selected_objects:
        obj.select_set(False)
    for obj in objects:
        obj.hide_set(False)
        obj.hide_viewport = False
        obj.select_set(True)
    bpy.context.view_layer.objects.active = objects[0]

operator = bpy.ops.export_scene.fbx
properties = operator.get_rna_type().properties
for field, value in [('axis_forward', '-Z'), ('axis_up', 'Y'),
                     ('apply_scale_options', 'FBX_SCALE_ALL'), ('armature_nodetype', 'NULL')]:
    assert value in [item.identifier for item in properties[field].enum_items]

common = dict(use_selection=True, global_scale=1.0,
              apply_unit_scale=True, apply_scale_options='FBX_SCALE_ALL',
              axis_forward='-Z', axis_up='Y', use_space_transform=True,
              bake_space_transform=False, add_leaf_bones=False,
              armature_nodetype='NULL', use_armature_deform_only=False,
              use_mesh_modifiers=False, mesh_smooth_type='FACE',
              path_mode='AUTO', embed_textures=False)

clip_names = ['1_Pose', 'Idle', 'Take Damage', 'Spawn', 'Death', 'Walk',
              'Attack_01', 'Attack_Bow_01']
activate(rig, bpy.data.actions['1_Pose'])
scene.frame_set(0)
metadata = {
    'source_file': work['blender_source'], 'revision': 6,
    'frameRate': 24.0, 'frameEnd': 76.0, 'releaseFrame': 57.0,
    'pickupFrame': 24.0,
    'arrowLength': max(vertex.co.z for vertex in bpy.data.objects['Arrow_IA'].data.vertices),
    'bones': [{'name': bone.name, 'parent': bone.parent.name if bone.parent else '',
               'head': list(bone.head_local), 'matrix': flat(bone.matrix_local)}
              for bone in rig.data.bones],
    'attachments': [], 'clips': [], 'samples': [], 'materials': []
}
for asset, source, bone in [('Sword', 'Sword', 'Hand.R'), ('Bow', 'Bow', 'Hand.L'),
                             ('Quiver', 'Quiver_IA', 'Spine_2')]:
    metadata['attachments'].append({'asset': asset, 'bone': bone,
                                     'sourceMatrix': flat(bpy.data.objects[source].matrix_world)})
for name in clip_names:
    action = bpy.data.actions[name]
    metadata['clips'].append({'name': name, 'start': float(action.frame_range[0]),
                               'end': max(1.0, float(action.frame_range[1]))})
for material in bpy.data.materials:
    node = next((n for n in material.node_tree.nodes if n.type == 'BSDF_PRINCIPLED'), None) if material.use_nodes else None
    color = list(node.inputs['Base Color'].default_value) if node else list(material.diffuse_color)
    metadata['materials'].append({'name': material.name, 'baseColor': color,
                                   'metallic': float(node.inputs['Metallic'].default_value) if node else 0.0,
                                   'roughness': float(node.inputs['Roughness'].default_value) if node else 0.6})

activate(rig, bpy.data.actions['Attack_Bow_01'])
arrow = bpy.data.objects['Arrow_IA']
activate(arrow, bpy.data.actions['Attack_Bow_01_Arrow'])
bow_keys = bpy.data.objects['Bow'].data.shape_keys
activate(bow_keys, bpy.data.actions['Attack_Bow_01_Bow'])
for step in range(305):
    frame = step / 4.0
    scene.frame_set(int(frame), subframe=frame % 1.0)
    metadata['samples'].append({'frame': frame,
                                'arrowMatrix': flat(arrow.matrix_world) if frame <= 57.0 else [],
                                'bowDraw': float(bow_keys.key_blocks['Bow_Draw_IA'].value)})

for asset_name, source_names in [('Sword', ['Sword']), ('Bow', ['Bow']),
                                 ('Quiver', ['Quiver_IA', 'Quiver_Arrows_IA']),
                                 ('Arrow', ['Arrow_IA'])]:
    temporary = []
    root = bpy.data.objects.new(asset_name + '_Export', None)
    scene.collection.objects.link(root)
    temporary.append(root)
    for source_name in source_names:
        source = bpy.data.objects[source_name]
        copy = source.copy()
        copy.data = source.data.copy()
        copy.name = source_name + '_Geometry'
        copy.animation_data_clear()
        copy.constraints.clear()
        copy.modifiers.clear()
        if copy.data.shape_keys:
            copy.data.shape_keys.animation_data_clear()
            for key in copy.data.shape_keys.key_blocks:
                key.value = 0.0
        scene.collection.objects.link(copy)
        copy.parent = root
        copy.matrix_parent_inverse = Matrix.Identity(4)
        copy.matrix_basis = Matrix.Identity(4)
        temporary.append(copy)
    for suffix, position in [('Origin', (0, 0, 0)), ('X', (1, 0, 0)),
                              ('Y', (0, 1, 0)), ('Z', (0, 0, 1))]:
        marker = bpy.data.objects.new('_Calibration' + suffix, None)
        scene.collection.objects.link(marker)
        marker.parent = root
        marker.location = position
        temporary.append(marker)
    select(temporary)
    operator(filepath=str(equipment_directory / (asset_name + '.fbx')),
             object_types={'MESH', 'EMPTY'}, bake_anim=False, **common)
    for obj in reversed(temporary):
        bpy.data.objects.remove(obj, do_unlink=True)

body = [rig] + [bpy.data.objects[name] for name in
               ['Body', 'Head', 'Jaw', 'Arm_L', 'Arm_R', 'Hand_L', 'Hand_R', 'Legs_L', 'Legs_R']]
rig.animation_data.action = None
rig.animation_data.use_nla = True
for track in rig.animation_data.nla_tracks:
    track.is_solo = False
    track.mute = False
    for strip in track.strips:
        strip.influence = 1.0
        strip.mute = False
scene.frame_set(0)
select(body)
operator(filepath=str(destination / 'SkeletonTesteIA.fbx'),
         object_types={'ARMATURE', 'MESH'}, bake_anim=True,
         bake_anim_use_all_actions=False, bake_anim_use_nla_strips=True,
         bake_anim_step=0.25, bake_anim_simplify_factor=0.0,
         bake_anim_force_startend_keying=True, bake_anim_use_all_bones=True, **common)
(destination / 'SkeletonExport.json').write_text(json.dumps(metadata, indent=2), encoding='utf-8')
print('SKELETON_EXPORT_OK ' + json.dumps({
    'destination': str(destination), 'clips': clip_names,
    'bone_count': len(rig.data.bones), 'accessory_samples': len(metadata['samples']),
    'files': [{'name': p.relative_to(destination).as_posix(), 'bytes': p.stat().st_size}
              for p in destination.rglob('*') if p.is_file() and p.suffix in {'.fbx', '.json'}]
}))
