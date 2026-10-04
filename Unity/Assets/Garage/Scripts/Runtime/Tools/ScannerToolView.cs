using System.Text;
using Garage.Sim.Ecu;
using Garage.Sim.Tools;

namespace Garage.Unity
{
    /// <summary>Handheld OBD-II scanner screen: VIN, MIL, codes and a live data list like a real scan tool.</summary>
    public sealed class ScannerToolView : DeviceScreen
    {
        protected override void Refresh()
        {
            var car = Runner.Car;
            var sb = new StringBuilder();
            if (Runner.Work == null || !Runner.Work.ScannerPlugged || !car.KeyOn)
            {
                Text.text = "<b>ELM-SIM v2.1</b>\n\nSin conexión con el vehículo.\nEnchufe el conector OBD y ponga el contacto.";
                return;
            }

            sb.AppendLine($"<b>OBD-II  {car.Definition.Vin}</b>   MIL: {(car.Ecu.Dtcs.MilOn ? "<color=#FFB000>ON</color>" : "OFF")}");
            foreach (string c in car.Ecu.Dtcs.ConfirmedCodes())
            {
                sb.AppendLine($"<color=#FFB000>{c}</color> {car.Ecu.Catalog.Get(c).DescriptionEs}");
            }

            foreach (string c in car.Ecu.Dtcs.PendingCodes())
            {
                sb.AppendLine($"{c} (pendiente)");
            }

            sb.AppendLine("──────────── DATOS EN VIVO ────────────");
            foreach (PidReading r in ScanLive(car))
            {
                sb.AppendLine($"{r.Definition.Name,-34} {ScanTool.FormatValue(r.Definition.Key, r.Value),8} {r.Definition.Unit}");
            }

            Text.text = sb.ToString();
        }

        private static System.Collections.Generic.IEnumerable<PidReading> ScanLive(Garage.Sim.Vehicle.Car car)
        {
            foreach (int pid in ScanTool.DefaultLivePids)
            {
                PidReading r = Obd2.ReadPid(car, pid);
                if (r != null)
                {
                    yield return r;
                }
            }
        }
    }
}
