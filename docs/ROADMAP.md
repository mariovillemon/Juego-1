# Hoja de ruta

## Fase 1 — Núcleo (hecho)
Motor gasolina turbo/atmosférico, sensores con curvas reales, red eléctrica nodal, ECU con lazo cerrado y
diagnóstico OBD-II, modos de fallo genéricos, herramientas, banco, meta-juego, CLI, 4 coches, proyecto Unity base.

## Fase 2 — Más sistemas
- ✅ **Hecho**:
  - Diésel Euro 6 con VGT, FAP (hollín, regeneraciones, P2463), monitor de EGR y NOx estimado.
  - GDI (bomba de alta y rampa).
  - VVT (P0010–P0012).
  - TPS y pedal de doble pista (P2135/P2138).
  - EVAP completo (P0440–P0457).
  - Cuatro coches nuevos.
- Pendiente:
  - Diésel: inyección piloto, sensor de NOx real, AdBlue/SCR y calentadores con módulo.
  - GDI: carbonilla en válvulas de admisión.
  - VVT de escape (P0013/P0014).
  - **Frenos** (ABS con sensores de rueda, C0xxx), **suspensión** (holguras, ruidos), **transmisión** (TCM,
    deslizamientos, P07xx) y **dirección**.
  - **CAN completo**: tramas reales con IDs, terminaciones de 120 Ω medibles, gateway y diagnóstico UDS
    (servicios 0x19/0x14/0x22/0x2E).
  - Sondas de oxígeno del banco 2 (V6/V8).
  - Rendimiento: solver eléctrico sin asignaciones y simulación multihilo de coches aparcados.
- Pulido de Unity:
  - Shader Graph de desgaste por capas.
  - Muestras de audio reales.
  - Traducción completa de todos los paneles (hoy sólo menús, opciones, pausa y HUD).
  - Soporte de mando en la UI.

## Fase 3 — Modding y Steam Workshop
- Editor de coches en juego (plantilla + overrides) con validación en vivo contra los schemas.
- Empaquetado de mods (`mod.json` + carpetas) y subida/descarga con la API de Steam Workshop (Steamworks.NET).
- Modelos 3D por slot visual en mods (AssetBundles/Addressables).
- Firmas/compatibilidad de versión de datos (`formatVersion` en contenido).

## Fase 4 — Híbridos y eléctricos
- Batería de alta tensión (celdas, BMS, desequilibrios, aislamiento P0AA6), inversor, motor eléctrico, convertidor DC/DC.
- Seguridad de alta tensión como mecánica (EPI, desconexión de servicio, medida de ausencia de tensión).
- Híbrido en paralelo/serie con gestión de energía y códigos P0Axx/P1xxx genéricos.
