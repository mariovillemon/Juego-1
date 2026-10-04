# Registro de decisiones

Cada decisión tomada sin consultar, con su motivo. Formato: **D-nn — título**: decisión. *Motivo.*

## Plataforma y estructura

- **D-01 — JSON propio en lugar de System.Text.Json/Newtonsoft**: `Garage.Data` incluye un parser/escritor JSON
  (RFC 8259 + comentarios `//`) y un validador de JSON Schema (subconjunto 2020-12). *Unity no incluye
  System.Text.Json y Newtonsoft obligaría a un paquete; un parser de ~600 líneas sin dependencias compila igual en
  .NET 8 y en Unity (IL2CPP incluido, sin reflexión). Se abstrae con `IJsonParser` por si un día se cambia.*
- **D-02 — C# 9 y sin `record`/`init`** en las librerías: *Unity 2021+ soporta C# 9, pero `init` necesita
  `IsExternalInit`, que no existe en netstandard2.1. Se usan clases con propiedades.*
- **D-03 — `TreatWarningsAsErrors` + documentación XML obligatoria** en Sim y Data. *Petición de "sin warnings".*
- **D-04 — La CLI usa C# 12 / net8.0**; sólo las librerías están limitadas a C# 9.
- **D-05 — Tests sobre el contenido real** (`data/base`). *Los tests de comportamiento emergente deben demostrar
  que los coches del juego se comportan bien, no un coche de laboratorio.*
- **D-06 — Unity 6000.0 LTS (Unity 6) con HDRP 17.0.x**. *Última LTS conocida. Ver `UNITY_SETUP.md`.*
- **D-07 — Paso fijo 0,02 s (50 Hz)**; los "viajes del cliente" y avances rápidos usan 0,05 s (estable y 4× más rápido).

## Simulación del motor

- **D-10 — Modelo de valor medio** (mean value engine model) con eventos de combustión discretos por cilindro.
  *Suficiente para síntomas de diagnóstico (mezcla, fallos, presiones) sin resolver la termodinámica por grado
  de cigüeñal, y rápido (≈230× tiempo real en Release).*
- **D-11 — Inyección indirecta (port injection)** para los gasolina, incluso en los turbo. *El caudal del inyector
  es función sólo de la presión de rampa (no de la del cilindro) y el banco de pruebas con manómetro tiene sentido.
  La inyección directa (GDI) queda en el roadmap.*
- **D-12 — MAP resuelto por bisección del equilibrio de caudales**: mariposa (orificio compresible) + fugas +
  EGR = aspiración del motor (VE·ρ·Vd·rpm/120). *Hace que una fuga de vacío produzca aire no medido de forma
  natural y que el ralentí dependa de verdad de la mariposa.*
- **D-13 — Turbo como presión con dinámica de 1.er orden** limitada por: capacidad según energía de escape, presión
  de apertura de la wastegate (muelle + duty de la N75) y caudal máximo del compresor (cae al cubo si se supera).
  *Las fugas de presión producen subpresión (P0299) porque el compresor no puede suministrar el caudal extra.*
- **D-14 — Límite de detonación (KLA)** = MBT + margen + 1,2°/RON − 9°/bar de MAP − 0,18°/°C de aire − …
  + enriquecimiento. Los mapas de avance de serie se generan con la misma fórmula menos 3° (turbo) o 1,5° (atmosférico).
  *Así los mapas son coherentes y un aumento de avance o de presión produce detonación real.*
- **D-15 — Rendimiento indicado** 0,385–0,40 × f(avance−MBT) × f(λ). Pérdida por avance 0,06 %/grado².
  *Heywood, cap. 9: ~5 % de par perdido a 10° de MBT.*
- **D-16 — Fricción** FMEP = 0,9 + 0,1·N + 0,025·N² bar (N en krpm) ×(1+frío). *Orden de magnitud de Heywood fig. 13-12.*
- **D-17 — Retardo de transporte del escape** 0,05 s + 240/rpm. *Imprescindible para que la sonda de banda estrecha
  oscile ~1 Hz como en la realidad (sin él el lazo converge sin conmutar).*
- **D-18 — Diésel experimental**: sin mariposa, par por cantidad inyectada, límite de humo por λ mínima, EGT=f(1/λ),
  calentadores, rail común. Faltan: FAP/regeneraciones, sensor NOx, inyección piloto. Marcado `experimental` en datos.

### Órdenes de magnitud y fuentes

| Magnitud | Valor usado | Referencia típica |
|---|---|---|
| Ralentí 2.0 T: MAP / MAF | 26–31 kPa / 2,7–3,3 g/s | 25–35 kPa, 2,5–4 g/s en motores de 2 L |
| 2.0 T de serie | ≈235–245 CV, 330–350 N·m | segmento "hot hatch" 2.0 turbo |
| 1.4 atmosférico | ≈90 CV, 125 N·m | utilitarios 1.4 16v |
| 2.0 TD | ≈150 CV, 330–350 N·m | berlinas 2.0 diésel |
| Presión de rampa (indirecta) | 3,5–4 bar | sistemas sin retorno 3–4 bar |
| Resistencia inyector alta impedancia | 12–16 Ω | hojas de datos de inyectores EV1/EV6/EV14 |
| Primario de bobina | 0,5–1 Ω | bobinas tipo lápiz |
| NTC refrigerante | ≈2–2,5 kΩ a 20 °C, ≈200–300 Ω a 90 °C | curva típica NTC M12 (B≈3450 K) |
| Sensor CKP inductivo | 500–1000 Ω, >0,5 V en arranque | sensores VR rueda 60-2 |
| Sonda de banda estrecha | 0,1–0,9 V, conmuta en λ=1 | sensores zirconio |
| Banda ancha (LSU 4.9) | Ip ≈ −0,5 mA (λ 0,9) … +0,6 mA (λ 1,2) | característica LSU 4.9 |
| Compresión | 11–14 bar en arrastre | manuales de taller |

Las cifras son órdenes de magnitud de dominio público (manuales de formación, Heywood *Internal Combustion Engine
Fundamentals*, Bosch *Automotive Handbook*), no copia de datos propietarios.

## Electricidad y ECU

- **D-20 — Cada circuito es una red resistiva resuelta por análisis nodal** (Gauss con pivoteo, fuentes como
  equivalentes Norton). *Los códigos de circuito, las lecturas del multímetro y los efectos de alta resistencia
  salen de la misma física; no hay tablas "fallo → tensión".*
- **D-21 — Umbrales de rango** 0,25 V / 4,7 V en sensores ratiométricos; 0,08 / 4,92 V en termistores.
  *Una masa abierta en un sensor de 3 hilos da ≈4,77 V por el pull-down de 100 kΩ: un umbral de 4,8 no lo detectaría.*
- **D-22 — Diagnóstico de actuadores por el driver de lado bajo**: tensión con el transistor apagado (debe ser ≈Vbat
  a través de la carga) y corriente encendido. *Es como lo hacen las ECU reales; permite códigos P0201 vs P0261/P0262.*
- **D-23 — Lógica OBD-II**: pendiente al primer fallo; confirmado tras `trips` ciclos consecutivos (1 en códigos de
  circuito, 2 en el resto); MIL; código permanente (modo 0A) que sólo se borra cuando el monitor vuelve a pasar;
  MIL apagada tras 3 ciclos buenos; olvido tras 40 calentamientos. *SAE J1979 / normativa EOBD.*
- **D-24 — Correcciones de combustible por celdas** (ralentí y crucero) con STFT y LTFT ±25 %. *Permite la técnica
  clásica de diagnóstico "LTFT alto sólo al ralentí = fuga de vacío".*
- **D-25 — Sonda de banda ancha en el ECU como tensión equivalente** (1,5 V a λ=1, ≈1,05 V/mA). *Simplifica el
  controlador de Ip sin perder el comportamiento observable.*
- **D-26 — Red CAN simplificada**: mensajes periódicos y timeout de 0,5 s; un ramal abierto aísla un módulo; un corto
  pone el bus en *bus off*. No se modelan tramas ni arbitraje.

## Contenido

- **D-30 — Marcas ficticias**: Aurex, Velmora, Nordak, Kessler. Motores "T20", "K14", "D20", "R6 3.0". VIN ficticios.
- **D-31 — El contenido base se genera con `tools/gen_content.py`** para que mapas, VE y KLA sean coherentes; los JSON
  resultantes son la fuente de verdad y se pueden editar a mano o con mods.
- **D-32 — `failure_modes.json` se exporta desde la librería integrada** (`garage export-modes`) y un test garantiza
  que coinciden. Los mods pueden añadir modos nuevos o sobrescribir parámetros.
- **D-33 — Magnitud de los modos de fallo de señal en "fracción del rango del sensor"**. *Así un mismo modo
  ("sesgada alta") vale para cualquier sensor.*
- **D-34 — Los clientes conducen el coche antes de traerlo** (dos ciclos de conducción simulados). *Los coches
  llegan con MIL, códigos confirmados y correcciones aprendidas, como en la vida real.*

## Meta-juego

- **D-40 — Economía**: 55 €/h, margen de piezas 25 %, 1 h de diagnosis al aceptar, alquiler 120 €/día. El cliente
  paga hasta un 10 % por encima del presupuesto. Las piezas usadas/aftermarket pueden fallar de nuevo (fallo latente
  intermitente programado con semilla).
- **D-41 — Evaluación objetiva**: prueba de carretera (MIL), verdad oculta (fallos restantes), ITV simulada
  (eficiencia de catalizador, λ, monitores) y banco (potencia, detonación, λ a plena carga, daño).
- **D-42 — Guardado JSON versionado** (`formatVersion` 1): los coches se reconstruyen desde definición + semilla y se
  reaplica el estado (fallos, piezas, daño, calibración, correcciones, memoria DTC).

## Unity

- **D-50 — Las fuentes de Garage.Sim/Garage.Data se copian a `Unity/Assets/Garage/Sim/Generated`** mediante
  `tools/sync-sim-to-unity.(sh|ps1)` con su propio `.asmdef` (`noEngineReferences: true`). *Depuración directa del
  código en Unity sin DLLs.* La copia está en `.gitignore`.
- **D-51 — Todo el contenido de escena se genera por scripts de editor idempotentes** (menú `Garage/…`): borran y
  recrean sólo los objetos bajo raíces conocidas (`__Workshop`, `__Lighting`…).
- **D-52 — Interfaces de herramientas con uGUI + TextMeshPro sobre RenderTexture** en lugar de UI Toolkit.
  *uGUI en world space es lo más probado para pantallas 3D dentro de la escena.*
- **D-53 — Shader de desgaste**: en lugar de un Shader Graph (archivo binario/JSON enorme difícil de generar a mano)
  se usa el material HDRP **Lit con capas** generado por script (`LayeredLit`) + **Decal Projectors** para suciedad,
  óxido y grasa, controlados desde `WearController` con los parámetros del coche. *Se puede compilar y mantener sin
  el editor; el Shader Graph queda como mejora.*
- **D-54 — Assets externos**: sólo CC0 de Poly Haven (texturas y HDRIs) descargados por menú, nunca versionados.

## Dudas abiertas sobre APIs de Unity (sin poder compilar)

- `HDAdditionalLightData`: en HDRP 17 la intensidad y unidad se fijan con `Light.intensity` + `Light.lightUnit`
  (API de `Light` de Unity 6). Se usa `LightUnit.Lumen`/`Lux` y `light.useColorTemperature`. Si una versión menor
  de HDRP no expone `lightUnit` en `Light`, el script cae a `HDAdditionalLightData.SetIntensity` mediante
  compilación condicional `#if` (ver `LightingBuilder.cs`).
- `VolumeProfile.Add<T>()` y overrides (`Exposure`, `Tonemapping`, `Bloom`, `ScreenSpaceAmbientOcclusion`,
  `ScreenSpaceReflection`, `ContactShadows`, `DepthOfField`, `FilmGrain`, `Vignette`, `VisualEnvironment`,
  `PhysicallyBasedSky`): nombres según HDRP 17. Revisar si una actualización los renombra.
- Creación del HDRP Asset por script: `ScriptableObject.CreateInstance<HDRenderPipelineAsset>()` y asignación en
  `GraphicsSettings.defaultRenderPipeline` + `QualitySettings.renderPipeline`. El Global Settings se crea con
  `HDRenderPipelineGlobalSettings` vía el asistente del paquete si no existe (el menú avisa).
- **D-55 — Mezclador de audio**: no existe API pública para crear un `.mixer` por script; `AudioBuses` aplica
  volúmenes por grupo (motor, taller, herramientas, ambiente, UI) y, si se asigna un AudioMixer creado a mano con
  grupos de esos nombres, enruta las fuentes a él. Reverb de nave con `AudioReverbZone` (preset Hangar).
- **D-56 — Sonido procedural provisional**: pulsos de combustión a la frecuencia real de encendido (rpm/120 × cilindros),
  huecos al fallar un cilindro, silbido del turbo y picado; las muestras CC0/CC-BY se mezclan por rpm cuando existan.
- **D-57 — Poly Haven**: se eligen por categoría los recursos más descargados (sin ids fijos en el código) para no
  romperse si cambia el catálogo. La red del entorno de desarrollo no permitía acceder a la API, así que la estructura
  JSON (`Diffuse`, `nor_gl`, `Rough`, `AO`, `Metal`, `hdri`) se basa en la documentación pública conocida.

## Sesión 3 — Jugable dentro de Unity

- **D-58 — Capa de aplicación `src/Garage.Game`** (netstandard2.1, C# 9, sin Unity): `GameSession` orquesta el ciclo
  completo (tablón, presupuesto, tienda e inventario, reparación, entrega, tiempo, mejoras, ranuras de guardado,
  tutorial) y `CarWork` todas las acciones sobre el coche (motor, herramientas, ECU, banco). La CLI y Unity usan
  exactamente esta capa; las reglas siguen en `Garage.Sim` (`Workshop`). Los comandos devuelven `CommandResult`
  con `CommandError` y texto legible; todo cambio se publica en un `GameEventBus` (la UI no sondea).
  Plazos de entrega por calidad: recambio y usada inmediatas, original al día siguiente, competición 2 días.
  Desde la CLI (y en Unity con «Comprar e instalar») se puede hacer una **compra urgente** al proveedor local
  al precio de catálogo, inmediata, para no bloquear una reparación.
- **D-59 — Protección del catalizador**: con un fallo de encendido que daña el catalizador (ventana de 200 vueltas,
  MIL intermitente) la ECU corta la inyección del cilindro afectado hasta parar el motor o borrar códigos, como
  hacen la mayoría de estrategias OEM. Además el daño térmico del catalizador se recalibró de ~40 s a varios minutos
  de sobretemperatura. Antes, un cliente que llegaba con una bobina muerta traía siempre el catalizador fundido.
- **D-60 — Eventos de presentación**: lo que sólo existe en Unity (andar, coger una herramienta, enchufar el
  escáner al OBD, abrir un panel) se notifica con `GameSession.Notify`. El estado lógico que dependa de ello
  (escáner enchufado) vive en `CarWork`; la CLI lo enchufa implícitamente.
- **D-61 — Tutorial basado en datos** (`data/base/tutorials.json` + `tutorial.schema.json`): pasos con el evento
  que los completa y un fragmento de asunto opcional; el `TutorialRunner` observa el bus. Crea su propio encargo
  guionizado (escenario `sc_coil2_dead`).
- **D-62 — Modelos procedurales con Blender en git**: los FBX que genera `tools/blender/models.py` pesan unos 3 MB y
  se versionan directamente, con una excepción a la regla LFS en `.gitattributes`. Así quien descarga el zip o no
  tiene Git LFS ve los modelos. Los modelos reales futuros (`.blend`, texturas) siguen yendo por LFS.
- **D-63 — Atrezo CC0 de Poly Haven**: *Download Free Assets* descarga además algunos modelos (herramientas,
  industrial, contenedores, mobiliario) en `Downloaded/Models`, y *Build Workshop Scene* los coloca en las
  estanterías. Son decorativos: el juego no depende de ellos y no se versionan.
- **D-64 — La prueba EVAP necesita vacío en el colector**: sólo arranca con MAP 25 kPa por debajo de la presión
  atmosférica, en lazo cerrado y caliente. Los turbo la hacen a carga ligera. Las fases son sellado, vaciado,
  mantenimiento y apertura. El tapón tiene una válvula de alivio (−3,5/+5 kPa) que limita el vacío del depósito.
- **D-65 — Tamaño de fuga por pendiente**: más de 0,118 kPa/s se considera fuga pequeña (P0442) y entre 0,035 y
  0,118 kPa/s, muy pequeña (P0456). Si no se llega a vacío y la purga mueve las correcciones, la fuga es grande
  (P0455, o P0457 si se ha repostado en ese viaje).
- **D-66 — Contrapresión de la geometría variable**: cerrar los álabes sube la presión de escape en proporción
  al caudal. Es lo que permite diagnosticar «VGT agarrotada cerrada» por el humo y la EGT, no sólo por la
  sobrepresión.
- **D-67 — Pista B invertida**: la pista B del TPS va invertida y la del pedal tiene media pendiente. El umbral
  de correlación es del 8 %, así que un sesgo pequeño (<0,3 V) no genera código. Es intencionado: no todo
  desajuste debe encender el testigo.
