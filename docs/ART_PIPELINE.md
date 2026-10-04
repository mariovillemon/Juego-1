# Pipeline de arte

## Convenciones generales
- **Escala**: 1 unidad = 1 metro. Un coche compacto mide ≈4,3 × 1,8 × 1,45 m; una bujía ≈ 2 cm de diámetro.
- **Ejes**: Y arriba, Z adelante (frontal del coche hacia +Z).
- **Exportación desde Blender** (FBX): *Apply Transform*, *Forward: -Z Forward*, *Up: Y Up*, escala 1,0 con
  *Apply Unit* activo y unidades de escena en metros; sin cámaras ni luces; *Smoothing: Face*; tangentes desde Unity.
- Orígenes: en el punto de anclaje funcional (centro de la brida de un sensor, eje de una rueda, centro del conector).

## Modelos procedurales con Blender (`tools/blender/`)

`tools/blender/models.py` genera con Python, sin interfaz, modelos de poligonización baja-media a escala real.
Cada modelo se exporta como FBX con LOD0, LOD1 y LOD2 (Decimate al 45 % y al 15 %) y colisiones `UCX_`
simplificadas. Se guardan en `Unity/Assets/Garage/Resources/`:

- `Engines/Engine_I4.fbx` y `Engine_I6.fbx`: bloque, culata, tapa de balancines con nervios, cárter, colector de
  admisión con tubos, colector de escape, caja de cambios y polea. Las cotas son las mismas que en
  `CarAssembler.BuildEngine`.
- `CarParts/<Kind>.fbx`: bobina, bujía, inyector, turbo, intercooler, alternador, batería, cuerpo de mariposa,
  centralita, relé, termostato, caja del filtro de aire, caudalímetro, catalizador, ventilador, los sensores (de
  caja y roscados) y los manguitos. Cada uno se construye en el marco del *placeholder* al que sustituye: mismo
  centro, orientación y tamaño.
- `Workshop/TwoPostLift.fbx`, `Workbench.fbx` y `ToolCart.fbx`.
- `CarBodies/Hatch.fbx` (`tools/blender/body.py`): carrocería genérica de compacto de 4,3 m con pasos de rueda y el
  vano motor abierto, suavizada con subdivisión, con las ruedas incluidas. Se usa en los coches con silueta
  *Hatch* que no tienen su propio `CarBodies/<carId>.fbx`. El material `CarPaint` toma el color del coche.

Para regenerarlos:

```bash
# Con Blender instalado (4.2 o superior)
blender --background --python tools/blender/models.py
# Sin Blender, con el módulo bpy (Python 3.11)
python -m venv .bpy && .bpy/bin/pip install bpy==4.2.0
.bpy/bin/python tools/blender/models.py            # opcional: carpeta de salida como argumento
.bpy/bin/python tools/blender/body.py              # carrocería de compacto
```

En Unity:

- `GarageModelPostprocessor` importa estos FBX a escala 1 sin animación. Los materiales salen de la descripción del
  FBX (HDRP/Lit), el LODGroup se crea a partir de los nombres `_LODn` y los `UCX_` se convierten en MeshCollider
  convexos.
- `CarAssembler` usa `Engines/Engine_I<n>` y `CarParts/<Kind>` si existen; si no, usa el *placeholder*.
- *Build Workshop Scene* coloca el elevador, los bancos y el carro.
- Los fusibles siguen siendo primitivas, porque su color indica el amperaje.

Los FBX generados pesan unos 3 MB en total y se guardan en git sin LFS (D-62).

## Nombres de mallas
```
<Slot>_<Parte>[_LOD<n>]      p. ej. injector_3_Body_LOD0, maf_Connector_LOD1
UCX_<Slot>_<n>               colisión simplificada convexa (importada como collider)
Connector_<pin…>             malla del conector para la interacción de enchufar/desenchufar
Bolt_<n>                     tornillos en el orden de apriete especificado
```
El `Slot` coincide con `visualSlot` del componente en los JSON (ver `data/base/templates`). Coloca el prefab en
`Assets/Garage/Resources/CarParts/<slot>.prefab` (o `<Kind>.prefab` para uno genérico): `CarAssembler` lo usa en
lugar del placeholder.

Carrocería completa: `Assets/Garage/Resources/CarBodies/<carId>.prefab` (origen en el suelo, centro del coche, +Z hacia delante, 1 u = 1 m, vano motor de 1,2 m abierto). Si no existe, `CarBodyBuilder` genera una procedural.

## LODs
| Elemento | LOD0 | LOD1 | LOD2 | Culling |
|---|---|---|---|---|
| Carrocería | 80–150 k tris | 40 k | 12 k | 3 % |
| Bloque motor | 40–60 k | 20 k | 6 k | 2 % |
| Piezas pequeñas (sensores, conectores) | 2–6 k | 1 k | — | 1 % |
| Mobiliario del taller | 5–20 k | 3 k | 800 | 1 % |

## Colisiones
Primitivas o `UCX_` convexas, nunca la malla visual. Piezas interactivas: un collider que cubra la zona de agarre.

## Texturas (PBR metálico, HDRP)
| Tipo de pieza | Resolución | Mapas |
|---|---|---|
| Carrocería (pintura) | 2K (+ capas de suciedad 2K tileables) | BaseColor, Normal, Mask (M/AO/Detalle/Suavidad) |
| Bloque/culata | 2K | ídem + mapa de grasa/suciedad |
| Sensores, conectores, tornillería | 512–1K | ídem |
| Neumáticos | 2K | ídem (con texto del flanco ficticio) |
| Mobiliario y elevador | 1–2K | ídem |
| Suelo/paredes del taller | 2K tileables + decals | ídem |

- BaseColor en sRGB; Normal como *Normal map* (convención OpenGL, Y+); Mask map lineal (R metálico, G AO,
  B máscara de detalle, A suavidad = 1 − rugosidad).
- Texel density objetivo: 10,24 px/cm en piezas que se miran de cerca, 5,12 px/cm en el resto.

## Fuentes gratuitas permitidas
- Poly Haven (CC0) — automatizado en *Garage/Assets/Download Free Assets*.
- Sketchfab con licencia **CC0 o CC-BY** (anota autor y URL en `ASSET_CREDITS.md`); evita marcas reales: borra logotipos.
- Recursos gratuitos de Fab con licencia que permita su uso en juegos comerciales.
- Modelado propio en Blender.
- **Nunca** marcas, logotipos o diseños reconocibles de fabricantes reales.
