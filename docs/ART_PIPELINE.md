# Pipeline de arte

## Convenciones generales
- **Escala**: 1 unidad = 1 metro. Un coche compacto mide ≈4,3 × 1,8 × 1,45 m; una bujía ≈ 2 cm de diámetro.
- **Ejes**: Y arriba, Z adelante (frontal del coche hacia +Z).
- **Exportación desde Blender** (FBX): *Apply Transform*, *Forward: -Z Forward*, *Up: Y Up*, escala 1,0 con
  *Apply Unit* activo y unidades de escena en metros; sin cámaras ni luces; *Smoothing: Face*; tangentes desde Unity.
- Orígenes: en el punto de anclaje funcional (centro de la brida de un sensor, eje de una rueda, centro del conector).

## Nombres de mallas
```
<Slot>_<Parte>[_LOD<n>]      p. ej. injector_3_Body_LOD0, maf_Connector_LOD1
UCX_<Slot>_<n>               colisión simplificada convexa (importada como collider)
Connector_<pin…>             malla del conector para la interacción de enchufar/desenchufar
Bolt_<n>                     tornillos en el orden de apriete especificado
```
El `Slot` coincide con `visualSlot` del componente en los JSON (ver `data/base/templates`). Coloca el prefab en
Carrocería completa: `Assets/Garage/Resources/CarBodies/<carId>.prefab` (origen en el suelo, centro del coche, +Z hacia delante, 1 u = 1 m, vano motor de 1,2 m abierto). Si no existe, `CarBodyBuilder` genera una procedural.

`Assets/Garage/Resources/CarParts/<slot>.prefab` (o `<Kind>.prefab` para uno genérico): `CarAssembler` lo usa en
lugar del placeholder.

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
