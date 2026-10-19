"""
Poly Haven's glTF models come with JPEG pictures, so a leaf's cut-out (its alpha) is not in them: the model's alpha map
is a picture of its own (<id>_alpha_1k.png, or <id>_<part>_alpha_1k.png for a part's material), which
build_station.py downloads next to the model's textures. attach_alpha() puts it back: each material whose alpha map
is there gets its colour picture and the alpha as one picture, linked to the material's alpha, so what prepares the
model (prepare_prop.py, bake_cards.py's render) cuts the leaves out. Runs inside Blender.
"""
import os

import bpy
import numpy as np


def _base_node(material):
    bsdf = next((n for n in material.node_tree.nodes if n.type == 'BSDF_PRINCIPLED'), None)
    if bsdf is None:
        return None, None
    socket = bsdf.inputs['Base Color']
    node = socket.links[0].from_node if socket.links else None
    # The importer may multiply the picture by the vertex colours: the picture is behind the multiply
    while node is not None and node.type != 'TEX_IMAGE':
        link = next((s.links[0] for s in node.inputs if s.links and s.links[0].from_node.type in ('TEX_IMAGE', 'MIX', 'MIX_RGB')), None)
        node = link.from_node if link else None
    return bsdf, node


def attach_alpha(objects, model_path):
    """The alpha maps next to the model (its folder's textures) put into the materials of these objects; the materials done"""
    folder = os.path.join(os.path.dirname(model_path), "textures")
    model_id = os.path.basename(os.path.dirname(model_path))
    done = []
    for o in objects:
        for slot in o.material_slots:
            material = slot.material
            if material is None or not material.use_nodes or material in done:
                continue
            bsdf, base = _base_node(material)
            if bsdf is None or base is None or base.image is None:
                continue
            # A part's own alpha map (island_tree_02_leaves_alpha), else the model's (shrub_03_alpha)
            candidates = [f"{material.name}_alpha_1k.png", f"{model_id}_alpha_1k.png"]
            if material.name.startswith(model_id + "_"):
                candidates.insert(0, f"{model_id}_{material.name[len(model_id) + 1:]}_alpha_1k.png")
            path = next((os.path.join(folder, c) for c in candidates if os.path.isfile(os.path.join(folder, c))), None)
            if path is None:
                continue
            alpha = bpy.data.images.load(path)
            colour = base.image
            if colour.size[0] != alpha.size[0] or colour.size[1] != alpha.size[1]:
                alpha.scale(colour.size[0], colour.size[1])
            merged = bpy.data.images.new(f"{colour.name}_cut", colour.size[0], colour.size[1], alpha=True)
            pixels = np.empty(colour.size[0] * colour.size[1] * 4, np.float32)
            colour.pixels.foreach_get(pixels)
            cut = np.empty(alpha.size[0] * alpha.size[1] * 4, np.float32)
            alpha.pixels.foreach_get(cut)
            pixels[3::4] = cut[0::4]
            merged.pixels.foreach_set(pixels)
            merged.pack()
            base.image = merged
            material.node_tree.links.new(base.outputs['Alpha'], bsdf.inputs['Alpha'])
            material.surface_render_method = 'DITHERED'
            material.use_backface_culling = False
            done.append(material)
            print(f"ALPHA {material.name}: {os.path.basename(path)}")
    return done
