# Puesta en marcha del proyecto Unity

**Versión asumida: Unity 6000.0.32f1 (Unity 6 LTS)** con HDRP 17.0.3, Input System 1.11, Cinemachine 3.1,
Timeline 1.8, Test Framework 1.4, uGUI 2.0 (incluye TextMeshPro). Cualquier 6000.0.x LTS posterior debería
servir (Unity Hub ofrecerá actualizar; acepta).

## Pasos tras clonar

1. **Git LFS**: `git lfs install` (los binarios de arte futuros van por LFS, ver `.gitattributes`).
2. **Sincronizar el núcleo y los datos**:
   - Linux/macOS: `tools/sync-sim-to-unity.sh`
   - Windows: `powershell -ExecutionPolicy Bypass -File tools/sync-sim-to-unity.ps1`

   Copia `src/Garage.Sim` y `src/Garage.Data` a `Unity/Assets/Garage/Sim/Generated/` (ensamblado `Garage.Sim`, sin
   referencias a Unity) y `data/` a `Unity/Assets/StreamingAssets/data/`. Repite tras cambiar el núcleo o los datos.
3. **Abrir** la carpeta `Unity/` en Unity Hub con la versión indicada. La primera importación tarda (HDRP compila shaders).
4. Si aparece el **HDRP Wizard**, pulsa *Fix All* (crea el *HDRP Global Settings* y recursos por defecto).
5. Menús, en este orden (o **Garage/Setup/Run All Setup Steps**):
   1. **Garage/Setup/Configure HDRP** — assets HDRP para presets Bajo/Medio/Alto/Ultra, perfil de Volume
      (exposición física automática, ACES, bloom sutil, SSAO, SSR, sombras de contacto, DoF, grano y viñeta leves,
      cielo físico), capa 31 `DeviceUI`, espacio de color lineal.
   2. **Garage/Setup/Build Main Menu Scene**
   3. **Garage/Setup/Build Dyno Scene**
   4. **Garage/Setup/Build Workshop Scene**
   5. **Garage/Assets/Download Free Assets** — texturas PBR y HDRI CC0 de Poly Haven (requiere red; sin red no cambia nada).
   6. **Garage/Setup/Bake Lighting and Occlusion** (opcional, mejora GI y rendimiento).
   7. **Garage/Validate/Check Scene** — escala, luces sin unidades físicas, materiales sin texturas, objetos sin colisión.
6. Abre `Assets/Garage/Scenes/Workshop.unity` y pulsa **Play**.

## Controles

| Acción | Tecla |
|---|---|
| Moverse / mirar | WASD / ratón (o mando) |
| Agacharse | Ctrl izq. |
| Lámpara de inspección | F |
| Inspeccionar pieza | E |
| Conectar/desconectar conector | C |
| Aflojar/apretar tornillos (en orden) | Clic izq. |
| Sustituir pieza (OEM) | R |
| Recepción: cliente, entrega, siguiente encargo | J |
| Editor de mapas (portátil) | Alt + flechas, Alt +/−, Tab cambia de mapa |

## Tests

*Window > General > Test Runner > EditMode*: generan las escenas y comprueban jerarquía, luces físicas,
colisiones y el puente Unity ↔ Garage.Sim.

## Problemas conocidos

- El proyecto se escribió sin poder abrir el editor (ver `DECISIONS.md`, "Dudas abiertas"). Si alguna API de HDRP
  cambió de nombre, el error aparecerá al importar en `Garage.Unity.Editor`; las partes más frágiles usan reflexión.
- Las unidades de luz se asignan por reflexión (`Light.lightUnit` en Unity 6, `HDAdditionalLightData.SetIntensity` antes).
- No se genera un `.mixer` (no hay API pública); `AudioBuses` hace de mezclador. Puedes crear un AudioMixer con grupos
  `Engine`, `Workshop`, `Tools`, `Ambience`, `UI` y asignarlo: las fuentes se enrutan solas.
