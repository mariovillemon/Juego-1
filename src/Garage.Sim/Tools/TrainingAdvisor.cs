using System;
using System.Collections.Generic;
using Garage.Sim.Ecu;
using Garage.Sim.Vehicle;

namespace Garage.Sim.Tools
{
    /// <summary>
    /// Training mode: explains diagnostic reasoning from what the player can observe (codes, live data, cues).
    /// It never reads the hidden faults — it teaches the method, not the answer.
    /// </summary>
    public static class TrainingAdvisor
    {
        /// <summary>Returns reasoning hints for the car's current observable state.</summary>
        public static List<string> Advise(Car car)
        {
            var tips = new List<string>();
            var codes = new List<string>(car.Ecu.Dtcs.ConfirmedCodes());
            foreach (string p in car.Ecu.Dtcs.PendingCodes())
            {
                if (!codes.Contains(p))
                {
                    codes.Add(p);
                }
            }

            IReadOnlyDictionary<string, double> live = car.Ecu.Live;
            double Get(string k) => live.TryGetValue(k, out double v) ? v : double.NaN;

            if (codes.Count == 0)
            {
                tips.Add("No hay códigos: confirma primero el síntoma (prueba de carretera, datos en vivo) antes de tocar nada. Un motor puede fallar sin código si el monitor aún no se ha ejecutado.");
            }

            foreach (string c in codes)
            {
                switch (c)
                {
                    case "P0171":
                    case "P0174":
                        tips.Add("P0171 = la ECU añade mucho combustible porque la sonda ve mezcla pobre. Compara LTFT al ralentí y en crucero: si sólo es alto al ralentí y baja con carga, piensa en aire no medido (fuga de vacío): usa la máquina de humo. Si sube con la carga, sospecha de falta de combustible: mide la presión de rampa a plena carga.");
                        break;
                    case "P0172":
                        tips.Add("P0172 = la ECU quita combustible. Busca combustible extra (inyector que gotea: presión residual que cae rápido) o aire medido que se pierde después del caudalímetro (fuga de presión de turbo).");
                        break;
                    case "P0300":
                    case "P0301":
                    case "P0302":
                    case "P0303":
                    case "P0304":
                    case "P0305":
                    case "P0306":
                        tips.Add($"{c}: identifica el cilindro (contadores de fallos en datos en vivo / prueba de equilibrado). Intercambia la bobina con otro cilindro: si el fallo se mueve, es la bobina; si no, mira bujía, inyector (osciloscopio/resistencia) y compresión.");
                        break;
                    case "P0128":
                        tips.Add("P0128: el motor tarda demasiado en llegar a temperatura. Mira ECT en datos en vivo tras 10–15 min: si se queda en 60–70 °C, el termostato está abierto. Antes, comprueba que el sensor ECT lee bien (compara con IAT en frío).");
                        break;
                    case "P0117":
                    case "P0118":
                    case "P0112":
                    case "P0113":
                        tips.Add($"{c} es un código de CIRCUITO: la tensión en la ECU está fuera de rango. Señal alta (≈5 V) en un termistor = circuito abierto (sensor o cable de señal/masa). Señal baja (≈0 V) = cortocircuito a masa. Desconecta el sensor y mide: tensión en el lado del arnés con contacto y resistencia del sensor.");
                        break;
                    case "P0107":
                    case "P0108":
                    case "P0122":
                    case "P0123":
                    case "P0102":
                    case "P0103":
                    case "P0237":
                    case "P0238":
                    case "P2122":
                    case "P2123":
                        tips.Add($"{c}: sensor de 3 hilos. Con contacto, en el conector del arnés debes medir ≈5 V en referencia, ≈0 V en masa (respecto a gnd) y la señal entre 0,5 y 4,5 V. Si falta la referencia, sigue el cable hacia la ECU midiendo continuidad.");
                        break;
                    case "P0335":
                        tips.Add("P0335: sin señal de cigüeñal la ECU no inyecta ni da chispa. Mide la resistencia del sensor inductivo (≈500–1000 Ω) y mira la señal con el osciloscopio durante el arranque (debe verse la rueda 60-2).");
                        break;
                    case "P0299":
                        tips.Add("P0299: presión de turbo insuficiente. Escucha soplidos al acelerar, haz prueba de humo con el sistema presurizado y comprueba que la electroválvula de wastegate recibe señal.");
                        break;
                    case "P0234":
                        tips.Add("P0234: sobrepresión. Wastegate agarrotada cerrada o electroválvula bloqueada. La ECU entra en modo emergencia para proteger el motor.");
                        break;
                    case "P0420":
                        tips.Add("P0420: compara la sonda anterior y la posterior. Con un catalizador sano la posterior está estable (~0,6–0,7 V); si copia a la anterior, el catalizador ya no almacena oxígeno. Descarta antes fugas de escape y fallos de encendido.");
                        break;
                    case "P0135":
                    case "P0141":
                        tips.Add($"{c}: calefactor de sonda. Mide la resistencia del calefactor (pocos ohmios) y la alimentación (fusible).");
                        break;
                    case "P0562":
                        tips.Add("P0562: tensión baja. Con el motor en marcha deberías tener 13,8–14,5 V en batería. Si no, alternador/correa; si sí, mira caídas de tensión en masas.");
                        break;
                    case "P0016":
                        tips.Add("P0016: correlación cigüeñal/árbol de levas. Típico de cadena estirada: compara las señales CKP y CMP con el osciloscopio de dos canales.");
                        break;
                    default:
                        DtcDefinition d = car.Ecu.Catalog.Get(c);
                        if (d.Causes.Count > 0)
                        {
                            tips.Add($"{c} ({d.DescriptionEs}): causas típicas {string.Join(", ", d.Causes)}. Verifica cada una con una medida antes de cambiar piezas.");
                        }

                        break;
                }
            }

            if (codes.Exists(x => x.StartsWith("U0", StringComparison.Ordinal)))
            {
                tips.Add("Códigos U: problema de comunicación. Lee todos los módulos: el que NO responde (o el que nadie oye) es el sospechoso. Revisa su alimentación, masa y el ramal CAN (≈60 Ω entre CAN-H y CAN-L con todo conectado).");
            }

            double stft = Get("stft");
            double ltft = Get("ltft");
            if (!double.IsNaN(stft) && !double.IsNaN(ltft) && Math.Abs(stft + ltft) > 15)
            {
                tips.Add($"Corrección total {stft + ltft:+0;-0} %: la ECU está compensando algo. Por encima de ±10 % merece investigación.");
            }

            if (car.Ecu.LimpMode)
            {
                tips.Add("Modo emergencia activo: la ECU limita mariposa/turbo. Resuelve el fallo y borra códigos para salir.");
            }

            tips.Add("Recuerda: cada medida cuesta tiempo. Lee códigos → datos en vivo → hipótesis → una medida que la confirme o la descarte → reparar → verificar.");
            return tips;
        }
    }
}
