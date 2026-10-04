# Comprobación de tipos de los scripts de Unity sin el editor

`./check.sh` compila `Unity/Assets/Garage/Scripts/Runtime` y `.../Editor` con .NET contra los ensamblados de
referencia reales de UnityEngine (paquete NuGet `UnityEngine.Modules` 2021.3) y *stand-ins* mínimos de las APIs de
paquetes (uGUI, TextMeshPro, Input System, HDRP, Cinemachine, UnityEditor) en `RuntimeStubs.cs`/`EditorStubs.cs`.

Detecta errores de nombres, tipos, `using` y ambigüedades. **No** sustituye a abrir el proyecto en Unity 6: los
stand-ins sólo declaran los miembros que usa el juego y el motor de referencia es 2021.3 (las APIs usadas existen
también en Unity 6). Si añades una llamada a una API de paquete nueva, añádela al stand-in con su firma real.
