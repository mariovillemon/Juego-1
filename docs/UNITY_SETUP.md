# Puesta en marcha del proyecto Unity

**Versión del proyecto: Unity 6000.0.84f1 (Unity 6 LTS)** con HDRP 17, Input System 1.11, Cinemachine 3.1,
Timeline 1.8, Test Framework 1.4 y uGUI 2.0 (incluye TextMeshPro). Cualquier 6000.0.x LTS posterior debería
servir: si Unity Hub avisa de que la versión es distinta, acepta.

## Pasos tras clonar o actualizar (`git pull`)

1. **Git LFS**: `git lfs install` (los binarios de arte futuros van por LFS, ver `.gitattributes`).
2. **Sincroniza el núcleo y los datos** cada vez que cambien `src/` o `data/`:
   - Linux/macOS: `tools/sync-sim-to-unity.sh`
   - Windows: `powershell -ExecutionPolicy Bypass -File tools/sync-sim-to-unity.ps1`

   Copia `src/Garage.Sim`, `src/Garage.Data` y **`src/Garage.Game`** (capa de aplicación compartida con la CLI) a
   `Unity/Assets/Garage/Sim/Generated/` (ensamblado `Garage.Sim`, sin referencias a Unity) y `data/` a
   `Unity/Assets/StreamingAssets/data/`.
3. **Abre** la carpeta `Unity/` en Unity Hub con la versión indicada. La primera importación tarda (HDRP compila shaders).
4. Si aparece el **HDRP Wizard**, pulsa *Fix All* y espera a que todo esté en verde.
5. Si aparece el **TMP Importer**, pulsa **Import TMP Essentials** (las fuentes de toda la interfaz). No hace falta
   *Examples & Extras*.
6. Menús, **en este orden** (o **Garage/Setup/Run All Setup Steps**, que hace del 1 al 4):
   1. **Garage/Setup/Configure HDRP**: crea los assets HDRP de los presets Bajo, Medio, Alto y Ultra y el perfil de
      Volume (exposición física automática, ACES, bloom sutil, SSAO, SSR, sombras de contacto, grano y viñeta
      leves, cielo físico). También crea la capa 31 `DeviceUI` y activa el espacio de color lineal.
   2. **Garage/Setup/Build Main Menu Scene**: menú principal (nueva partida, tutorial sí/no, modo formación o
      realista, cargar ranura, banco libre, salir).
   3. **Garage/Setup/Build Dyno Scene**: sala de banco en modo libre.
   4. **Garage/Setup/Build Workshop Scene**: el taller jugable. Incluye herramientas que se cogen (escáner,
      multímetro y osciloscopio en el banco; manómetro, compresímetro, comprobador de fugas y máquina de humo en el
      carro), puestos usables (ordenador de recepción, catálogo de recambios, catálogo de herramientas, portátil,
      consola del banco, caja de piezas viejas), el banco de rodillos con su anclaje y la interfaz del jugador
      (`__UI`).
   5. **Garage/Assets/Download Free Assets**: texturas PBR y HDRI CC0 de Poly Haven. Requiere red; sin red no cambia nada.
   6. **Garage/Setup/Bake Lighting and Occlusion**: opcional, mejora la iluminación global y el rendimiento.
   7. **Garage/Validate/Check Scene**: revisa escala, luces sin unidades físicas, materiales sin texturas y
      objetos sin colisión.
7. Abre `Assets/Garage/Scenes/MainMenu.unity` (o directamente `Workshop.unity`) y pulsa **Play**.
   Si abres `Workshop.unity` directamente, empieza una partida nueva con el tutorial.

> Tras cualquier `git pull` vuelve a ejecutar el paso 2 y **Garage/Setup/Build Workshop Scene**: la escena se
> regenera desde código y lo que hayas cambiado a mano en ella se pierde.

## Cómo se juega (ciclo completo)

1. **Tab**: abre el tablón de encargos. Elige un cliente, revisa su queja, el presupuesto máximo y el plazo, y envía
   un presupuesto. También puedes prepararlo línea a línea en la hoja de trabajo. El cliente puede aceptar, regatear
   o irse. Al aceptar, su coche sube al elevador.
2. **Diagnosticar**:
   - Coge el escáner (E), llévalo al conector OBD bajo el salpicadero (lado conductor) y enchúfalo (E). Después pon
     el contacto (K) y abre su pantalla (E sobre el escáner enchufado).
   - Desde el escáner: leer y borrar códigos, freeze frame, datos en vivo con gráfica, readiness y pruebas de actuadores.
   - **K** gira la llave: contacto → arranque → parar. **T** hace una prueba de carretera y **V** abre las acciones
     del coche.
   - Con el multímetro en la mano, clic sobre una pieza: vista de su conector con los pines numerados. Desde ahí
     colocas la punta roja y la negra (masa y batería incluidas), eliges lado mazo (*back-probe*), componente o ECU,
     desconectas el conector y mides.
   - Osciloscopio y pruebas mecánicas: con la herramienta en la mano, clic sobre el coche. **L** abre el portátil
     con los esquemas.
3. **Comprar**: **P** abre la tienda, filtrada por el coche, la categoría y la calidad (original, recambio, usada),
   con precio, fiabilidad y plazo. Añades al carrito y compras.
4. **Reparar**: apunta a la pieza y pulsa **R** (o E). Eliges el recambio del almacén (o una compra urgente) y
   mantienes pulsado cada paso: conector, tornillos, extraer, colocar, apretar al par en orden y conectar. **Shift**
   acelera. La pieza vieja va a la caja (**I**), donde puedes inspeccionarla.
5. **Reprogramar y banco**: **L** abre el portátil. Para escribir hace falta la interfaz de reprogramación y que el
   escáner esté enchufado al OBD. En la consola del banco, el botón «llevar al banco» coloca el coche en los rodillos.
   La pasada se reproduce en tiempo real; puedes compararla con las anteriores, ver cualquier canal y exportar a CSV.
6. **Entregar**: **J** abre la hoja de trabajo y pulsas «Entregar el coche y cobrar». Se hace la prueba final y ves
   la valoración y las consecuencias.
7. **Esc** abre la pausa: guardar o cargar en 5 ranuras, cerrar el día, ayuda de teclas y salir al menú.

## Controles

| Acción | Tecla |
|---|---|
| Moverse / mirar / agacharse / linterna | WASD / ratón / Ctrl izq. / F |
| Usar, coger, enchufar al OBD, abrir pieza | E |
| Soltar la herramienta en la superficie apuntada | G |
| Desenchufar el escáner del OBD | Q |
| Usar la herramienta de la mano sobre una pieza | Clic izq. |
| Sustituir pieza | R |
| Conector de la pieza (conectar/desconectar) | C |
| Llave: contacto → arranque → parar | K |
| Prueba de carretera / acciones del coche | T / V |
| Tablón / hoja de trabajo / tienda / almacén / herramientas / portátil | Tab / J / P / I / U / L |
| Ayuda de teclas / pausa | F1 / Esc |

## Tests

- *Window > General > Test Runner > EditMode*: generan las escenas y comprueban la jerarquía, las luces físicas y
  las colisiones. También comprueban que la escena tiene la UI, las herramientas y los puestos, el puerto OBD del
  coche y el ciclo de juego completo con los datos de StreamingAssets.
- Sin el editor: `tools/unity-typecheck/check.sh` compila todos los scripts de Unity (runtime, editor y tests)
  contra los ensamblados de referencia de UnityEngine y *stand-ins* de los paquetes. Se ejecuta también en CI.

## Problemas conocidos

- El proyecto se escribe sin el editor (ver `DECISIONS.md`, «Dudas abiertas»). El comprobador de tipos detecta
  errores de nombres y tipos, pero no los de comportamiento.
- Las unidades de luz se asignan por reflexión (`Light.lightUnit` en Unity 6, `HDAdditionalLightData.SetIntensity`
  antes).
- No se genera un `.mixer`, porque no hay API pública para crearlo; `AudioBuses` hace de mezclador. Si creas un
  AudioMixer con los grupos `Engine`, `Workshop`, `Tools`, `Ambience` y `UI`, las fuentes se enrutan solas.
