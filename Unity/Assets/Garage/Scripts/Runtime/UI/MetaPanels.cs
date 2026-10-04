using System.Collections.Generic;
using System.Linq;
using Garage.Game;
using Garage.Sim.Components;
using Garage.Sim.Game;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Garage.Unity.UI
{
    /// <summary>Job board: customers with car, symptom, goal, budget, patience and deadline; quote or reject.</summary>
    public sealed class BoardPanel : UiPanel
    {
        private RectTransform _list;
        private TextMeshProUGUI _detail;
        private TMP_InputField _amount;
        private Job _selected;

        protected override string Title => "Tablón de encargos";

        protected override void Build()
        {
            RectTransform cols = UiKit.Row(Body, "Cols", 0, 12);
            UiKit.Size(cols, flexibleHeight: 1);
            _list = UiKit.Scroll(cols, "Offers");
            UiKit.Size(_list.parent.parent, preferredWidth: 620);
            RectTransform right = UiKit.Column(cols, "Detail", 8);
            UiKit.Size(right, flexibleWidth: 1, flexibleHeight: 1);
            _detail = UiKit.Label(right, "Selecciona un encargo.", UiTheme.FontBody);
            _detail.alignment = TextAlignmentOptions.TopLeft;
            UiKit.Size(_detail, flexibleHeight: 1);
            RectTransform row = UiKit.Row(right, "QuoteRow");
            UiKit.Size(UiKit.Label(row, "Presupuesto (€)", UiTheme.FontSmall, UiTheme.TextDim), preferredWidth: 150);
            _amount = UiKit.Input(row, "", true, 140);
            UiKit.Button(row, "Enviar presupuesto", Send, UiTheme.ButtonPrimary);
            RectTransform row2 = UiKit.Row(right, "Row2");
            UiKit.Button(row2, "Preparar hoja de trabajo", () =>
            {
                if (_selected != null)
                {
                    Ui.Worksheet.ShowQuote(_selected);
                }
            });
            UiKit.Button(row2, "Rechazar encargo", () =>
            {
                if (_selected != null)
                {
                    Report(Runner.Session.RejectOffer(_selected));
                    _selected = null;
                    Refresh();
                }
            }, UiTheme.ButtonDanger);
        }

        public override void Open()
        {
            base.Open();
            Runner.Session.Notify(GameEventKind.UiOpened, "ui:board");
        }

        public override void Refresh()
        {
            GameSession s = Runner.Session;
            UiKit.Clear(_list);
            List<Job> offers = s.Offers(3);
            foreach (Job j in offers)
            {
                Job job = j;
                Button b = UiKit.Button(_list, $"{j.Customer.Name} — {j.Car.Definition.DisplayName}  ·  {Goal(j.Definition)}", () => Select(job), j == _selected ? UiTheme.RowSelected : UiTheme.Row);
                UiKit.Size(b, 44);
                b.GetComponentInChildren<TextMeshProUGUI>().alignment = TextAlignmentOptions.MidlineLeft;
            }

            if (_selected == null || _selected.Status != JobStatus.Offered)
            {
                _selected = offers.FirstOrDefault();
                if (_selected != null)
                {
                    _amount.text = s.SuggestQuote(_selected).ToString("0");
                }
            }

            ShowDetail();
        }

        private void Select(Job j)
        {
            _selected = j;
            _amount.text = Runner.Session.SuggestQuote(j).ToString("0");
            Refresh();
        }

        private void ShowDetail()
        {
            if (_selected == null)
            {
                _detail.text = "No hay encargos.";
                return;
            }

            Job j = _selected;
            CustomerProfile c = j.Customer;
            _detail.text =
                $"<b>{c.Name}</b>\n<color=#aaa>{c.Description}</color>\n\n" +
                $"<b>Coche:</b> {j.Car.Definition.DisplayName} · {j.Car.OdometerKm:0} km · {j.Car.Definition.Engine.Name}\n" +
                $"<b>Dice:</b> «{j.Definition.Complaint}»\n" +
                $"<b>Objetivo:</b> {GoalText(j.Definition)}\n" +
                $"<b>Presupuesto máximo orientativo:</b> {System.Math.Max(c.Budget, j.Definition.Budget):0} €\n" +
                $"<b>Paciencia:</b> {c.PatienceDays} días · <b>plazo:</b> {j.Definition.DeadlineDays} días\n" +
                $"<b>Carácter:</b> {Personality(c.Personality)}";
        }

        private void Send()
        {
            if (_selected == null)
            {
                return;
            }

            QuoteDecision d = Runner.Session.SendQuote(_selected, UiKit.ParseNumber(_amount.text));
            Ui.Toast(d.Message, d.Answer != QuoteAnswer.Rejected);
            if (d.Answer == QuoteAnswer.Counter)
            {
                _amount.text = d.Amount.ToString("0");
            }

            Refresh();
        }

        public static string Goal(JobDefinition d) => d.Goal switch
        {
            JobGoal.Power => "Potencia",
            JobGoal.Track => "Circuito",
            JobGoal.Inspection => "ITV",
            _ => "Avería",
        };

        public static string GoalText(JobDefinition d) => d.Goal switch
        {
            JobGoal.Power => $"más potencia (≥ {d.PowerTargetPs:0} CV) sin comprometer la fiabilidad",
            JobGoal.Track => $"preparación para circuito (≥ {d.PowerTargetPs:0} CV, pasadas seguidas)",
            JobGoal.Inspection => "pasar la inspección técnica (emisiones, sin MIL, monitores listos)",
            _ => "reparar la avería",
        };

        private static string Personality(string p) => p switch
        {
            "haggler" => "regatea",
            "enthusiast" => "aficionado, paga por calidad",
            "demanding" => "exigente",
            "impatient" => "impaciente",
            _ => "honesto",
        };
    }

    /// <summary>
    /// Work sheet: for an offered job, build a quote line by line (parts and labour) and send it; for the job on
    /// the lift, the provisional invoice and the delivery button. Also switches which accepted car is on the lift.
    /// </summary>
    public sealed class WorksheetPanel : UiPanel
    {
        private RectTransform _lines;
        private TextMeshProUGUI _header, _total;
        private RectTransform _actions;
        private QuoteDraft _quote;
        private TMP_InputField _hours, _search;

        protected override string Title => "Hoja de trabajo";

        protected override void Build()
        {
            _header = UiKit.Label(Body, "", UiTheme.FontBody);
            UiKit.Size(_header, 80);
            _lines = UiKit.Scroll(Body, "Lines");
            _total = UiKit.Label(Body, "", UiTheme.FontTitle, UiTheme.Text, TextAlignmentOptions.MidlineRight);
            UiKit.Size(_total, 40);
            _actions = UiKit.Column(Body, "Actions", 6);
        }

        /// <summary>Opens in quote mode for an offered job.</summary>
        public void ShowQuote(Job job)
        {
            _quote = Runner.Session.NewQuote(job);
            Open();
        }

        public override void Open()
        {
            base.Open();
            Runner.Session.Notify(GameEventKind.UiOpened, "ui:worksheet");
        }

        public override void Close()
        {
            base.Close();
            _quote = null;
        }

        public override void Refresh()
        {
            UiKit.Clear(_lines);
            UiKit.Clear(_actions);
            GameSession s = Runner.Session;
            if (_quote != null && _quote.Job.Status == JobStatus.Offered)
            {
                RefreshQuote(s);
                return;
            }

            _quote = null;
            Job j = s.ActiveJob;
            RectTransform pick = UiKit.Row(_actions, "Pick");
            foreach (Job a in s.ActiveJobs)
            {
                Job job = a;
                UiKit.Button(pick, a.Car.Definition.DisplayName, () => { Report(Runner.Load(job)); Refresh(); }, a == j ? UiTheme.RowSelected : UiTheme.Button);
            }

            if (j == null)
            {
                _header.text = "No hay ningún coche en el elevador. Acepta un encargo en el tablón (Tab).";
                _total.text = "";
                return;
            }

            _header.text = $"<b>{j.Car.Definition.DisplayName}</b> de {j.Customer.Name} — «{j.Definition.Complaint}»\nObjetivo: {BoardPanel.GoalText(j.Definition)} · presupuesto aceptado {j.QuotedAmount:0} € · tiempo empleado {j.LabourMinutes:0} min";
            foreach (InvoiceLine l in j.Lines)
            {
                Line(l.Description, l.Amount);
            }

            double labour = j.LabourMinutes / 60.0 * s.Workshop.LabourRate;
            Line($"(Tiempo total en el coche: {j.LabourMinutes:0} min = {labour:0.00} € de mano de obra)", double.NaN);
            _total.text = $"Piezas y trabajos facturados: {j.Lines.Sum(l => l.Amount):0.00} €  ·  máx. cobrable {j.QuotedAmount * 1.1:0} €";
            RectTransform row = UiKit.Row(_actions, "Deliver");
            UiKit.Button(row, "Entregar el coche y cobrar", () => Ui.Delivery.Deliver(j), UiTheme.ButtonPrimary);
            UiKit.Button(row, "Bajar del elevador", () => { Report(s.SetActiveJob(null)); Refresh(); });
        }

        private void RefreshQuote(GameSession s)
        {
            Job j = _quote.Job;
            _header.text = $"<b>Presupuesto para {j.Customer.Name}</b> — {j.Car.Definition.DisplayName}\n«{j.Definition.Complaint}» · {BoardPanel.GoalText(j.Definition)}";
            foreach (QuoteLine l in _quote.Lines.ToList())
            {
                QuoteLine line = l;
                RectTransform row = Line(l.Description + (l.Hours > 0 ? $" ({l.Hours:0.#} h)" : ""), l.Amount);
                UiKit.Button(row, "Quitar", () => { _quote.Remove(line); Refresh(); }, UiTheme.ButtonDanger, 90);
            }

            _total.text = $"Total: {_quote.Total:0.00} €";
            RectTransform add = UiKit.Row(_actions, "AddLabour");
            UiKit.Size(UiKit.Label(add, "Mano de obra (h)", UiTheme.FontSmall, UiTheme.TextDim), preferredWidth: 150);
            _hours = UiKit.Input(add, "1", true, 90);
            UiKit.Button(add, $"Añadir horas ({s.Workshop.LabourRate:0} €/h)", () => { _quote.AddLabour("Mano de obra", UiKit.ParseNumber(_hours.text, 1)); Refresh(); });
            RectTransform addPart = UiKit.Row(_actions, "AddPart");
            UiKit.Size(UiKit.Label(addPart, "Pieza (buscar)", UiTheme.FontSmall, UiTheme.TextDim), preferredWidth: 150);
            _search = UiKit.Input(addPart, "", false, 260);
            UiKit.Button(addPart, "Añadir la más barata que coincida", () =>
            {
                PartDefinition p = s.Catalog(j.Car.Definition.Id, null, null, _search.text).FirstOrDefault();
                if (p == null)
                {
                    Ui.Toast("No hay piezas que coincidan.", false);
                    return;
                }

                _quote.AddPart(p, s.Workshop.PartsMarkup);
                Refresh();
            });
            RectTransform send = UiKit.Row(_actions, "Send");
            UiKit.Button(send, "Enviar al cliente", () =>
            {
                QuoteDecision d = s.SendQuote(_quote);
                Ui.Toast(d.Message, d.Answer != QuoteAnswer.Rejected);
                if (d.Answer != QuoteAnswer.Counter)
                {
                    _quote = null;
                }

                Refresh();
            }, UiTheme.ButtonPrimary);
        }

        private RectTransform Line(string text, double amount)
        {
            RectTransform row = UiKit.Row(_lines, "Line", 30);
            TextMeshProUGUI d = UiKit.Label(row, text, UiTheme.FontSmall);
            UiKit.Size(d, flexibleWidth: 1);
            TextMeshProUGUI a = UiKit.Label(row, double.IsNaN(amount) ? "" : $"{amount:0.00} €", UiTheme.FontBody, UiTheme.Text, TextAlignmentOptions.MidlineRight);
            UiKit.Size(a, preferredWidth: 140);
            return row;
        }
    }

    /// <summary>Delivery summary: final test, invoice, customer opinion and consequences.</summary>
    public sealed class DeliveryPanel : UiPanel
    {
        private TextMeshProUGUI _text;

        protected override string Title => "Entrega";

        protected override Vector2 SizeFraction => new Vector2(0.55f, 0.6f);

        protected override void Build()
        {
            _text = UiKit.Label(Body, "", UiTheme.FontBody);
            _text.alignment = TextAlignmentOptions.TopLeft;
            UiKit.Size(_text, flexibleHeight: 1);
            UiKit.Button(Body, "Cerrar", Close, UiTheme.ButtonPrimary);
        }

        /// <summary>Delivers a job and shows the outcome.</summary>
        public void Deliver(Job job)
        {
            JobOutcome o = Runner.Session.Deliver(job, out CommandResult r);
            if (o == null)
            {
                Report(r);
                return;
            }

            string verdict = o.Success ? $"<color={UiTheme.Hex(UiTheme.Ok)}><b>TRABAJO TERMINADO — el cliente queda satisfecho</b></color>" : $"<color={UiTheme.Hex(UiTheme.Bad)}><b>EL CLIENTE NO QUEDA SATISFECHO</b></color>";
            string notes = string.Join("\n", o.Notes.Select(n => "• " + n));
            string truth = "";
            if (Runner.Session.Training)
            {
                truth = "\n\n<color=#aaa>Averías reales (modo formación):\n" + string.Join("\n", job.Car.Faults.All.Where(f => f.Origin != "test").Select(f => $"   {f.Mode.Name} en {job.Car.Parts.Get(f.ComponentId)?.Name} — {(f.Repaired ? "reparada" : "SIN REPARAR")}")) + "</color>";
            }

            _text.text = $"{verdict}\n\n{job.Customer.Name} recoge su {job.Car.Definition.DisplayName}.\n\nCobrado: <b>{o.Payment:0.00} €</b>   Reputación {o.ReputationDelta:+0.0;-0.0}\n\n{notes}{truth}";
            Ui.Worksheet.Close();
            Open();
        }
    }

    /// <summary>Parts shop: filters (fits the car on the lift, category, quality, text), cart and checkout.</summary>
    public sealed class ShopPanel : UiPanel
    {
        private RectTransform _list, _cart;
        private TextMeshProUGUI _cartTotal;
        private TMP_InputField _search;
        private bool _onlyCar = true;
        private PartQuality? _quality;
        private Button _carToggle, _qualityToggle;

        protected override string Title => "Tienda de piezas";

        protected override void Build()
        {
            RectTransform filters = UiKit.Row(Body, "Filters");
            UiKit.Size(UiKit.Label(filters, "Buscar", UiTheme.FontSmall, UiTheme.TextDim), preferredWidth: 70);
            _search = UiKit.Input(filters, "", false, 260);
            _search.onValueChanged.AddListener(_ => Refresh());
            _carToggle = UiKit.Button(filters, "", () => { _onlyCar = !_onlyCar; Refresh(); });
            _qualityToggle = UiKit.Button(filters, "", () =>
            {
                _quality = _quality == null ? PartQuality.Oem : _quality == PartQuality.Oem ? PartQuality.Aftermarket : _quality == PartQuality.Aftermarket ? PartQuality.Used : _quality == PartQuality.Used ? PartQuality.Performance : (PartQuality?)null;
                Refresh();
            });
            RectTransform cols = UiKit.Row(Body, "Cols", 0, 12);
            UiKit.Size(cols, flexibleHeight: 1);
            _list = UiKit.Scroll(cols, "Catalog");
            RectTransform right = UiKit.Column(cols, "Cart", 6);
            UiKit.Size(right, preferredWidth: 420, flexibleHeight: 1);
            UiKit.Size(UiKit.Label(right, "<b>Carrito</b>", UiTheme.FontBody), 28);
            _cart = UiKit.Scroll(right, "CartLines");
            _cartTotal = UiKit.Label(right, "", UiTheme.FontBody);
            UiKit.Size(_cartTotal, 30);
            UiKit.Button(right, "Comprar", () => { Report(Runner.Session.Checkout()); Refresh(); }, UiTheme.ButtonPrimary);
        }

        public override void Open()
        {
            base.Open();
            Runner.Session.Notify(GameEventKind.UiOpened, "ui:shop");
        }

        /// <summary>Opens filtered by text (e.g. from a component).</summary>
        public void OpenFor(string text)
        {
            Open();
            _search.text = text;
        }

        public override void Refresh()
        {
            GameSession s = Runner.Session;
            string carId = s.ActiveJob?.Car.Definition.Id;
            UiKit.SetText(_carToggle, _onlyCar && carId != null ? "Sólo compatibles con el coche" : "Todas las piezas");
            UiKit.SetText(_qualityToggle, _quality == null ? "Calidad: todas" : "Calidad: " + Garage.Game.Inventory.QualityText(_quality.Value));
            UiKit.Clear(_list);
            foreach (PartDefinition p in s.Catalog(_onlyCar ? carId : null, null, _quality, _search.text).Take(120))
            {
                PartDefinition part = p;
                RectTransform row = UiKit.Row(_list, p.Id, 34);
                TextMeshProUGUI n = UiKit.Label(row, $"{p.Name}  <color=#999>{Garage.Game.Inventory.QualityText(p.Quality)} · fiab. {p.Reliability:P0} · entrega {Garage.Game.Inventory.LeadTimeText(p.Quality)} · stock {s.Inventory.Count(p.Id)}</color>", UiTheme.FontSmall);
                UiKit.Size(n, flexibleWidth: 1);
                UiKit.Size(UiKit.Label(row, $"{p.Price:0.00} €", UiTheme.FontBody, UiTheme.Text, TextAlignmentOptions.MidlineRight), preferredWidth: 110);
                UiKit.Button(row, "+", () => { s.AddToCart(part.Id); Refresh(); }, UiTheme.ButtonPrimary, 44);
            }

            UiKit.Clear(_cart);
            foreach (CartLine l in s.Inventory.Cart)
            {
                CartLine line = l;
                RectTransform row = UiKit.Row(_cart, "Cart", 30);
                UiKit.Size(UiKit.Label(row, $"{l.Quantity}× {l.Part.Name}", UiTheme.FontSmall), flexibleWidth: 1);
                UiKit.Size(UiKit.Label(row, $"{l.Total:0.00} €", UiTheme.FontSmall), preferredWidth: 90);
                UiKit.Button(row, "-", () => { s.RemoveFromCart(line.Part.Id); Refresh(); }, UiTheme.ButtonDanger, 36);
            }

            _cartTotal.text = $"Total {s.Inventory.CartTotal:0.00} €  ·  caja {s.Money:0} €";
        }
    }

    /// <summary>Stock, pending orders and the old-parts box (bench inspection).</summary>
    public sealed class InventoryPanel : UiPanel
    {
        private RectTransform _stock, _old;

        protected override string Title => "Almacén y caja de piezas viejas";

        protected override void Build()
        {
            RectTransform cols = UiKit.Row(Body, "Cols", 0, 12);
            UiKit.Size(cols, flexibleHeight: 1);
            RectTransform a = UiKit.Column(cols, "A", 6);
            UiKit.Size(a, flexibleWidth: 1, flexibleHeight: 1);
            UiKit.Size(UiKit.Label(a, "<b>Almacén</b> (y pedidos en camino)"), 28);
            _stock = UiKit.Scroll(a, "Stock");
            RectTransform b = UiKit.Column(cols, "B", 6);
            UiKit.Size(b, flexibleWidth: 1, flexibleHeight: 1);
            UiKit.Size(UiKit.Label(b, "<b>Piezas desmontadas</b> (inspeccionar en banco: 5 min)"), 28);
            _old = UiKit.Scroll(b, "Old");
        }

        public override void Refresh()
        {
            GameSession s = Runner.Session;
            UiKit.Clear(_stock);
            foreach (KeyValuePair<string, int> kv in s.Inventory.Stock)
            {
                PartDefinition p = s.Content.Parts.Get(kv.Key);
                UiKit.Size(UiKit.Label(_stock, $"{kv.Value}× {p?.Name ?? kv.Key}", UiTheme.FontSmall), 26);
            }

            foreach (PartOrder o in s.Inventory.Orders)
            {
                PartDefinition p = s.Content.Parts.Get(o.PartId);
                UiKit.Size(UiKit.Label(_stock, $"<color=#999>{o.Quantity}× {p?.Name ?? o.PartId} — llega el día {o.ArrivalDay}</color>", UiTheme.FontSmall), 26);
            }

            UiKit.Clear(_old);
            for (int i = 0; i < s.Inventory.OldParts.Count; i++)
            {
                int idx = i;
                RemovedPart p = s.Inventory.OldParts[i];
                RectTransform row = UiKit.Row(_old, "Old", 34);
                string verdict = p.Inspected ? $" — <color={UiTheme.Hex(p.WasFaulty ? UiTheme.Bad : UiTheme.Ok)}>{p.Finding}</color>" : "";
                UiKit.Size(UiKit.Label(row, $"{p.Name} (día {p.Day}){verdict}", UiTheme.FontSmall), flexibleWidth: 1);
                if (!p.Inspected)
                {
                    UiKit.Button(row, "Inspeccionar", () => { Report(s.InspectOldPart(idx)); Refresh(); }, UiTheme.Button, 140);
                }
            }
        }
    }

    /// <summary>Tools and workshop upgrades, unlocked with money and reputation.</summary>
    public sealed class UpgradesPanel : UiPanel
    {
        private RectTransform _list;
        private TextMeshProUGUI _info;

        protected override string Title => "Herramientas y mejoras del taller";

        protected override void Build()
        {
            _info = UiKit.Label(Body, "", UiTheme.FontBody);
            UiKit.Size(_info, 30);
            _list = UiKit.Scroll(Body, "List");
        }

        public override void Refresh()
        {
            GameSession s = Runner.Session;
            _info.text = $"Caja {s.Money:0} € · reputación {s.Reputation:0}/100 — la reputación sube entregando bien y a tiempo.";
            UiKit.Clear(_list);
            foreach (UpgradeDefinition u in s.Content.Upgrades)
            {
                UpgradeDefinition up = u;
                RectTransform row = UiKit.Row(_list, u.Id, 44);
                bool owned = s.Workshop.Has(u.Id);
                bool locked = s.Reputation < u.ReputationRequired;
                UiKit.Size(UiKit.Label(row, $"<b>{u.Name}</b>  <color=#999>{u.Description}</color>", UiTheme.FontSmall), flexibleWidth: 1);
                UiKit.Size(UiKit.Label(row, owned ? "<color=#7c7>EN PROPIEDAD</color>" : $"{u.Price:0} € · rep ≥ {u.ReputationRequired:0}", UiTheme.FontSmall, locked && !owned ? UiTheme.TextDim : UiTheme.Text), preferredWidth: 190);
                if (!owned)
                {
                    Button b = UiKit.Button(row, "Comprar", () => { Report(s.BuyUpgrade(up.Id)); Refresh(); }, UiTheme.ButtonPrimary, 120);
                    b.interactable = !locked;
                }
            }
        }
    }

    /// <summary>Pause menu: continue, save/load slots, close the day, quit to the main menu.</summary>
    public sealed class PausePanel : UiPanel
    {
        private RectTransform _slots;
        private bool _saving = true;
        private Button _mode;

        protected override string Title => "Pausa";

        protected override Vector2 SizeFraction => new Vector2(0.5f, 0.75f);

        protected override void Build()
        {
            UiKit.Button(Body, "Continuar", Close, UiTheme.ButtonPrimary);
            UiKit.Button(Body, "Cerrar el taller por hoy (pasar al día siguiente)", () => { Runner.Session.EndDay(); Close(); });
            _mode = UiKit.Button(Body, "", () => { _saving = !_saving; Refresh(); });
            _slots = UiKit.Scroll(Body, "Slots");
            UiKit.Button(Body, "Ayuda de teclas", () => Ui.Toast(GameUI.HelpText, true, 12f));
            UiKit.Button(Body, "Salir al menú principal", () =>
            {
                UiState.Reset();
                UnityEngine.SceneManagement.SceneManager.LoadScene("MainMenu");
            }, UiTheme.ButtonDanger);
        }

        public override void Open()
        {
            base.Open();
            Time.timeScale = 0f;
        }

        public override void Close()
        {
            base.Close();
            Time.timeScale = 1f;
        }

        public override void Refresh()
        {
            UiKit.SetText(_mode, _saving ? "Modo: GUARDAR (pulsa para cambiar a cargar)" : "Modo: CARGAR (pulsa para cambiar a guardar)");
            UiKit.Clear(_slots);
            foreach (SaveSlotInfo info in Runner.Slots.List())
            {
                SaveSlotInfo slot = info;
                string label = $"Ranura {info.Slot}: {info.Summary}" + (info.Used ? $"  <color=#999>({info.SavedAt:dd/MM HH:mm})</color>" : "");
                UiKit.Button(_slots, label, () =>
                {
                    if (_saving)
                    {
                        Runner.Slots.Save(Runner.Session, slot.Slot);
                    }
                    else if (slot.Used)
                    {
                        Report(Runner.Slots.Load(Runner.Session, slot.Slot));
                    }

                    Refresh();
                }, UiTheme.Row);
            }
        }
    }
}
