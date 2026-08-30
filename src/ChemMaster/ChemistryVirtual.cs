using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Ss14.Chemistry;

// Offline model only: no process reader, OS input, BUI messages or game connection.
internal static class ChemistryVirtual
{
    internal static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    // Rules are immutable for the lifetime of the process. A replan can therefore
    // never silently switch to JSON replaced after the user approved the job.
    private static readonly Lazy<GameChemistryRules> PinnedRules = new(
        ReadRulesFromDisk, System.Threading.LazyThreadSafetyMode.ExecutionAndPublication);

    internal static GameChemistryRules LoadRules() => PinnedRules.Value;

    private static GameChemistryRules ReadRulesFromDisk() => JsonSerializer.Deserialize<GameChemistryRules>(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "chemistry-game-rules.json")), Json)
        ?? throw new InvalidDataException("Нет игровых правил химии.");

    internal static int Cents(decimal amount)
    {
        if (amount < 0 || amount * 100 != decimal.Truncate(amount * 100) || amount > int.MaxValue / 100m)
            throw new VirtualStop("invalid-amount", "Объём должен быть неотрицательным, с точностью до 0.01u.");
        return checked((int)(amount * 100));
    }

    internal static CalibrationProfile ReadCalibration(string path)
    {
        var profile = JsonSerializer.Deserialize<CalibrationProfile>(File.ReadAllText(path), Json)
            ?? throw new InvalidDataException("Пустая калибровка.");
        var errors = ChemCalibration.Validate(profile);
        if (errors.Count != 0) throw new VirtualStop("invalid-calibration", string.Join("; ", errors));
        if (profile.View != "input") throw new VirtualStop("invalid-calibration", "Нужна разметка вкладки «Вход».");
        return profile;
    }

    public static int Run(string path, bool json)
    {
        path = Path.GetFullPath(path);
        var scenario = JsonSerializer.Deserialize<VirtualScenario>(File.ReadAllText(path), Json)
            ?? throw new InvalidDataException("Пустой сценарий.");
        if (scenario.SchemaVersion != 1) throw new InvalidDataException("Неподдерживаемая версия сценария.");
        var profile = scenario.Calibration == null ? null : ReadCalibration(
            Path.GetFullPath(scenario.Calibration, Path.GetDirectoryName(path)!));
        var rules = LoadRules();
        var machine = new VirtualChemMaster(rules, scenario.Buffer, scenario.BeakerCapacity, profile,
            ChemistryPlanning.ChemicalNames()) { Powered = scenario.Powered, HasBeaker = scenario.HasBeaker };
        machine.SetSorting(scenario.Sorting);
        foreach (var reagent in scenario.Beaker) machine.Beaker.Add(reagent.Prototype, Cents(reagent.Amount));
        var results = new List<VirtualJobResult>();
        foreach (var job in scenario.Jobs)
        {
            var result = Execute(machine, job);
            results.Add(result);
            if (result.Status != "completed") break;
        }
        if (scenario.Jobs.Count == 0) throw new InvalidDataException("В сценарии нет jobs.");
        var report = new
        {
            schemaVersion = 1, offlineOnly = true, scenario.Name, gameRevision = rules.Revision,
            calibrationValidated = profile != null, results,
            finalBuffer = machine.Buffer.Export(), finalBeaker = machine.Beaker.Export(),
            limitations = new[] { "Это модель, не интеграционный тест клиента/сервера SS14.",
                "Нет фасовки таблеток/бутылок, внешнего нагрева, выливания drag-and-drop, эффектов реакций и reagent data.",
                "Прокрутка и задержки UI только виртуальные. Геометрия не доказывает попадание в живые кнопки.",
                "Правила взяты из публичного commit; версия активного сервера может отличаться." }
        };
        if (json) Console.WriteLine(JsonSerializer.Serialize(report, Json));
        else
        {
            Console.WriteLine($"Виртуальный Химмастер — {scenario.Name}; SS220 {rules.Revision[..12]}");
            foreach (var result in results)
                Console.WriteLine($"{result.Status}: {result.Request}; действий {result.Actions.Count}. {result.Detail}");
            Console.WriteLine("Буфер: " + string.Join("; ", machine.Buffer.Export().Select(x => $"{x.Prototype}={x.Amount:0.##}")));
            Console.WriteLine("Только офлайн-модель; игра не использовалась.");
        }
        return results.All(x => x.Status == "completed") ? 0 : 4;
    }

    internal static VirtualJobResult Execute(VirtualChemMaster machine, VirtualJob job,
        Action<int, VirtualChemMaster>? beforeApply = null)
    {
        var initial = machine.Buffer.Export();
        ChemistryPlanning.ChemistryPlanOutput? plan = null;
        var executed = new List<VirtualAction>();
        try
        {
            machine.CheckReady();
            if (machine.Beaker.Volume != 0) throw new VirtualStop("beaker-not-empty", "Сначала нужна чистая входная ёмкость.");
            if (job.Mode != "ensure" && job.Mode != "make") throw new VirtualStop("invalid-request", "Режим: ensure или make.");
            plan = ChemistryPlanning.BuildForSimulation(job.Request, machine.Buffer.Inventory(), gameRules: machine.Rules);
            if (plan.Requested.Count == 0 || plan.Warnings.Any(x => x.StartsWith("Не удалось сопоставить") ||
                x.StartsWith("Неизвестная категория") || x.Contains("должен быть больше нуля")))
                throw new VirtualStop("invalid-request", "Не все цели распознаны: " + string.Join("; ", plan.Warnings));
            var goals = plan.Requested.GroupBy(x => x.Prototype, StringComparer.Ordinal)
                .ToDictionary(x => x.Key, x => x.Sum(v => Cents(v.Amount)), StringComparer.Ordinal);
            if (job.Mode == "make")
            {
                foreach (var id in goals.Keys.ToArray()) goals[id] = checked(goals[id] + machine.Buffer.Get(id));
                string request = string.Join(";", goals.Select(x => x.Key + "=" + (x.Value / 100m).ToString(CultureInfo.InvariantCulture)));
                plan = ChemistryPlanning.BuildForSimulation(request, machine.Buffer.Inventory(), gameRules: machine.Rules);
            }
            if (plan.BaseRequirements.Count != 0)
                throw new VirtualStop("needs-reagents", "Не хватает: " + string.Join("; ", plan.BaseRequirements.Select(x => $"{x.Prototype}={x.Amount:0.##}")));

            // Validate the complete job on a clone first, including reactions after EACH
            // button press. A preflight failure leaves the caller's virtual inventory intact.
            var trial = machine.Clone();
            var commands = new List<VirtualCommand>();
            ChemistryPlanning.PlanStepOutput? externalPreparation = null;
            foreach (var step in plan.Steps)
            {
                if (step.RequiresExternalApparatus || step.GasProducts.Count != 0)
                {
                    PrepareExternalStep(trial, step, commands);
                    externalPreparation = step;
                    break;
                }
                Produce(trial, step, commands);
            }
            if (externalPreparation == null)
            {
                if (goals.Any(x => trial.Buffer.Get(x.Key) < x.Value))
                    throw new VirtualStop("target-not-reached", "Проверка итогового состава не подтвердила все цели.");
                if (trial.Beaker.Volume != 0) throw new VirtualStop("return-incomplete", "Не всё возвращено в буфер.");
            }
            else if (trial.Beaker.Volume == 0)
                throw new VirtualStop("external-preparation-empty", externalPreparation.Prototype +
                    ": внешний этап не оставил подготовленную смесь во входной мензурке.");

            for (int i = 0; i < commands.Count; i++)
            {
                beforeApply?.Invoke(i, machine);
                executed.Add(machine.Apply(commands[i]));
            }
            var detail = externalPreparation == null
                ? "Цели проверены, содержимое мензурки возвращено в буфер."
                : ExternalPreparationDetail(externalPreparation);
            return new VirtualJobResult(job.Request, "completed", detail,
                plan, initial, machine.Buffer.Export(), executed);
        }
        catch (VirtualStop stop)
        {
            return new VirtualJobResult(job.Request, stop.Code, stop.Message, plan, initial, machine.Buffer.Export(), executed);
        }
    }

    private static void Produce(VirtualChemMaster machine, ChemistryPlanning.PlanStepOutput step,
        List<VirtualCommand> commands)
    {
        if (step.RequiresExternalApparatus || step.GasProducts.Count != 0)
            throw new VirtualStop("external-condition", step.Prototype + ": нужны внешние условия/эффекты; автоматического нагрева нет.");
        var rule = machine.Rules.Reactions.FirstOrDefault(rule => Matches(step, rule));
        if (rule == null) throw new VirtualStop("recipe-mismatch", step.Prototype + ": план вики не совпал с игровым рецептом.");
        if (rule.HasEffects || rule.MixerCategories.Count != 0)
            throw new VirtualStop("unsupported-reaction", rule.Id + ": эффекты или внешнее смешивающее устройство.");
        var yield = rule.Outputs.Single(x => x.Prototype == step.Prototype).Amount;
        decimal remaining = step.TargetAmount / yield;
        int quantum = Enumerable.Range(1, 100).First(n => rule.Inputs.Where(x => !x.Catalyst)
            .All(x => x.Amount * n == decimal.Truncate(x.Amount * n)));
        // BuildForSimulation has already rounded an intermediate target to a whole
        // stoichiometric batch. Rounding its repeat count again would overproduce it.
        decimal catalysts = rule.Inputs.Where(x => x.Catalyst).Sum(x => x.Amount);
        decimal perRepeat = Math.Max(rule.Inputs.Where(x => !x.Catalyst).Sum(x => x.Amount), rule.Outputs.Sum(x => x.Amount));
        decimal batchMax = decimal.Floor((machine.Capacity / 100m - catalysts) / perRepeat / quantum) * quantum;
        if (batchMax < quantum) throw new VirtualStop("capacity-too-small", step.Prototype + ": даже минимальная кнопочная партия не помещается.");
        while (remaining > 0)
        {
            decimal maximum = Math.Min(remaining, batchMax);
            if (maximum % quantum != 0) throw new VirtualStop("unreachable-dose", "Партия не выражается точными кнопочными дозами.");
            // The final ingredient may need ONE button press: splitting carbon into
            // several clicks can turn freshly made Inaprovaline into Bicaridine.
            var sizes = new HashSet<decimal> { maximum, quantum };
            foreach (var input in rule.Inputs.Where(x => !x.Catalyst))
                foreach (int dose in new[] { 1, 5, 10, 15, 20, 25, 30, 50, 75, 100 })
                {
                    decimal n = dose / input.Amount;
                    if (n <= maximum && n >= quantum && n % quantum == 0) sizes.Add(n);
                }
            List<VirtualCommand>? accepted = null;
            decimal acceptedSize = 0;
            VirtualStop? failure = null;
            foreach (decimal repeats in sizes.OrderByDescending(x => x))
            {
                foreach (var order in Orders(step.Inputs.Select(x => x.Prototype).ToList()).Take(120))
                {
                    var batch = machine.Clone();
                    var candidate = new List<VirtualCommand>();
                    try
                    {
                        var expected = rule.Outputs.ToDictionary(x => x.Prototype, x => Cents(x.Amount * repeats), StringComparer.Ordinal);
                        foreach (var catalyst in rule.Inputs.Where(x => x.Catalyst))
                            expected[catalyst.Prototype] = checked(expected.GetValueOrDefault(catalyst.Prototype) + Cents(catalyst.Amount));
                        foreach (string id in order)
                        {
                            var input = rule.Inputs.Single(x => x.Prototype == id);
                            TransferExact(batch, id, Cents(input.Amount * (input.Catalyst ? 1 : repeats)), candidate);
                        }
                        if (!SameContents(batch.Beaker, expected))
                            throw new VirtualStop("unexpected-reaction", step.Prototype + ": получился другой состав или реакция не завершилась.");
                        while (batch.Beaker.Items.Count > 0)
                            AddCommand(batch, batch.Beaker.Items[0].Prototype, "all", false, candidate);
                        accepted = candidate;
                        acceptedSize = repeats;
                        break;
                    }
                    catch (VirtualStop stop) { failure = stop; }
                }
                if (accepted != null) break;
            }
            if (accepted == null) throw failure ?? new VirtualStop("no-safe-sequence", "Не найдена проверенная кнопочная последовательность.");
            if (commands.Count + accepted.Count > 10000) throw new VirtualStop("action-limit", "Слишком большой сценарий.");
            foreach (var command in accepted) { machine.Apply(command); commands.Add(command); }
            remaining -= acceptedSize;
        }
    }

    private static void PrepareExternalStep(VirtualChemMaster machine,
        ChemistryPlanning.PlanStepOutput step, List<VirtualCommand> commands)
    {
        var rule = machine.Rules.Reactions.FirstOrDefault(rule => Matches(step, rule));
        if (rule == null)
            throw new VirtualStop("recipe-mismatch", step.Prototype +
                ": план вики не совпал с игровым рецептом внешнего этапа.");
        var target = rule.Outputs.Single(x => x.Prototype == step.Prototype);
        var repeats = step.TargetAmount / target.Amount;
        if (repeats <= 0)
            throw new VirtualStop("invalid-plan", step.Prototype + ": неверный объём внешнего этапа.");

        var required = rule.Inputs.Select(input => new
        {
            input.Prototype,
            Amount = Cents(input.Amount * (input.Catalyst ? 1 : repeats)),
        }).ToList();
        var total = checked(required.Sum(input => input.Amount));
        if (total > machine.Capacity - machine.Beaker.Volume)
            throw new VirtualStop("capacity-too-small", step.Prototype +
                ": ингредиенты внешнего этапа не помещаются во входную мензурку.");

        foreach (var input in required)
            TransferExact(machine, input.Prototype, input.Amount, commands);
    }

    private static string ExternalPreparationDetail(ChemistryPlanning.PlanStepOutput step)
    {
        var conditions = new List<string>();
        if (step.MinimumTemperatureKelvinExclusive.HasValue)
            conditions.Add("нагреть выше " + step.MinimumTemperatureKelvinExclusive.Value.ToString("0.##", CultureInfo.InvariantCulture) + " K");
        if (step.MaximumTemperatureKelvinExclusive.HasValue)
            conditions.Add("охладить ниже " + step.MaximumTemperatureKelvinExclusive.Value.ToString("0.##", CultureInfo.InvariantCulture) + " K");
        if (!step.Operation.Equals("mix", StringComparison.OrdinalIgnoreCase))
            conditions.Add(step.ActionText);
        if (step.GasProducts.Count != 0)
            conditions.Add("учесть выделение газа: " + string.Join(", ", step.GasProducts.Select(gas => gas.Name)));
        if (conditions.Count == 0) conditions.Add("выполнить внешний этап вручную");
        return "Ингредиенты для «" + step.DisplayName + "» собраны во входной мензурке. Дальше вручную: " +
            string.Join("; ", conditions) + ".";
    }

    private static IEnumerable<List<string>> Orders(List<string> values)
    {
        if (values.Count == 0) { yield return new List<string>(); yield break; }
        for (int i = 0; i < values.Count; i++)
            foreach (var suffix in Orders(values.Where((_, j) => j != i).ToList()))
            {
                suffix.Insert(0, values[i]);
                yield return suffix;
            }
    }

    private static bool Matches(ChemistryPlanning.PlanStepOutput step, GameReaction rule)
    {
        var target = rule.Outputs.FirstOrDefault(x => x.Prototype == step.Prototype);
        if (target == null || target.Amount <= 0 || rule.Inputs.Count != step.Inputs.Count || rule.Outputs.Count != step.Byproducts.Count + 1) return false;
        decimal scale = step.TargetAmount / target.Amount;
        return rule.Inputs.All(x => step.Inputs.Any(y => y.Prototype == x.Prototype && y.Catalyst == x.Catalyst &&
            y.Amount == x.Amount * (x.Catalyst ? 1 : scale))) &&
            step.Byproducts.All(y => rule.Outputs.Any(x => x.Prototype == y.Prototype && y.Amount == x.Amount * scale));
    }

    internal static bool SameContents(VirtualSolution solution, IReadOnlyDictionary<string, int> expected) =>
        solution.Items.Count == expected.Count && expected.All(x => solution.Get(x.Key) == x.Value);

    private static void TransferExact(VirtualChemMaster machine, string id, int amount, List<VirtualCommand> commands)
    {
        if (machine.Buffer.Get(id) < amount) throw new VirtualStop("needs-reagents", "Недостаточно " + id + " при пошаговой проверке.");
        int remaining = amount;
        while (remaining > 0)
        {
            string dose;
            if (remaining == machine.Buffer.Get(id)) dose = "all";
            else
            {
                int n = new[] { 100, 75, 50, 30, 25, 20, 15, 10, 5, 1 }.FirstOrDefault(x => x * 100 <= remaining);
                if (n == 0) throw new VirtualStop("unreachable-dose", id + ": дробь нельзя точно отмерить кнопками из этого остатка.");
                dose = n.ToString(CultureInfo.InvariantCulture);
            }
            int moved = AddCommand(machine, id, dose, true, commands).AmountHundredths;
            if (moved <= 0 || moved > remaining) throw new VirtualStop("transfer-mismatch", "Перенесён неверный объём.");
            remaining -= moved;
        }
    }

    private static VirtualAction AddCommand(VirtualChemMaster machine, string id, string dose, bool fromBuffer, List<VirtualCommand> commands)
    {
        if (commands.Count >= 10000) throw new VirtualStop("action-limit", "Слишком большой сценарий (>10000 нажатий).");
        var command = machine.Prepare(id, dose, fromBuffer);
        var action = machine.Apply(command);
        commands.Add(command);
        return action;
    }
}

internal sealed class VirtualStop : Exception
{
    public string Code { get; }
    public VirtualStop(string code, string message) : base(message) { Code = code; }
}

internal sealed class VirtualScenario
{
    public int SchemaVersion { get; set; } = 1;
    public string Name { get; set; } = "scenario";
    public string? Calibration { get; set; }
    public decimal BeakerCapacity { get; set; } = 100;
    public bool Powered { get; set; } = true;
    public bool HasBeaker { get; set; } = true;
    public string Sorting { get; set; } = "none";
    public List<VirtualReagent> Buffer { get; set; } = new();
    public List<VirtualReagent> Beaker { get; set; } = new();
    public List<VirtualJob> Jobs { get; set; } = new();
}
internal sealed class VirtualJob
{
    public string Request { get; set; } = "";
    public string Mode { get; set; } = "ensure";
}
internal sealed record VirtualReagent(string Prototype, decimal Amount);
internal sealed record VirtualQuantity(string Prototype, int Amount);
internal sealed record VirtualCommand(string ExpectedState, string Prototype, string Dose, bool FromBuffer, int RowIndex,
    int FirstVisibleRow, bool ScrollRequired, CalibrationPoint? Point);
internal sealed record VirtualAction(string Prototype, string Dose, bool FromBuffer, int RowIndex, int FirstVisibleRow,
    bool ScrollRequired, CalibrationPoint? Point, int AmountHundredths, List<VirtualReagent> BufferAfter,
    List<VirtualReagent> BeakerAfter, float Temperature, List<string> Reactions);
internal sealed record VirtualJobResult(string Request, string Status, string Detail,
    ChemistryPlanning.ChemistryPlanOutput? Plan, List<VirtualReagent> InitialBuffer,
    List<VirtualReagent> FinalBuffer, List<VirtualAction> Actions);

internal sealed class VirtualSolution
{
    public List<VirtualQuantity> Items { get; } = new();
    public float Temperature { get; set; } = 293.15f;
    public int Volume => checked(Items.Sum(x => x.Amount));
    public int Get(string id) => Items.FirstOrDefault(x => x.Prototype == id)?.Amount ?? 0;
    public void Add(string id, int amount)
    {
        if (amount < 0 || string.IsNullOrWhiteSpace(id)) throw new VirtualStop("invalid-amount", "Неверный реагент/объём.");
        if (amount == 0) return;
        int i = Items.FindIndex(x => x.Prototype == id);
        if (i < 0) Items.Add(new VirtualQuantity(id, amount));
        else Items[i] = Items[i] with { Amount = checked(Items[i].Amount + amount) };
        _ = Volume;
    }
    public int Remove(string id, int amount, bool preserveOrder)
    {
        if (amount < 0) throw new VirtualStop("invalid-amount", "Отрицательное переливание.");
        int i = Items.FindIndex(x => x.Prototype == id);
        if (i < 0 || amount == 0) return 0;
        int taken = Math.Min(amount, Items[i].Amount);
        if (taken == Items[i].Amount)
        {
            if (!preserveOrder) Items[i] = Items[^1]; // Solution.RemoveSwap for the input container.
            Items.RemoveAt(preserveOrder ? i : Items.Count - 1);
        }
        else Items[i] = Items[i] with { Amount = Items[i].Amount - taken };
        return taken;
    }
    public Dictionary<string, decimal> Inventory() => Items.ToDictionary(x => x.Prototype, x => x.Amount / 100m, StringComparer.Ordinal);
    public List<VirtualReagent> Export() => Items.Select(x => new VirtualReagent(x.Prototype, x.Amount / 100m)).ToList();
}

internal sealed class VirtualChemMaster
{
    public GameChemistryRules Rules { get; }
    public VirtualSolution Buffer { get; } = new();
    public VirtualSolution Beaker { get; } = new();
    public int Capacity { get; }
    public bool Powered { get; set; } = true;
    public bool HasBeaker { get; set; } = true;
    public string Mode { get; set; } = "transfer";
    public string Sorting { get; private set; } = "none";
    public CalibrationProfile? Profile { get; }
    private readonly IReadOnlyDictionary<string, string> _names;
    private int _bufferScroll, _inputScroll;

    public VirtualChemMaster(GameChemistryRules rules, IEnumerable<VirtualReagent> stock, decimal capacity = 100,
        CalibrationProfile? profile = null, IReadOnlyDictionary<string, string>? names = null)
    {
        Rules = rules;
        Capacity = ChemistryVirtual.Cents(capacity);
        if (Capacity <= 0) throw new VirtualStop("invalid-capacity", "Нужна положительная вместимость.");
        if (rules.SchemaVersion != 1) throw new InvalidDataException("Неподдерживаемые игровые правила.");
        Profile = profile;
        _names = names ?? new Dictionary<string, string>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in stock)
        {
            if (!ids.Add(row.Prototype)) throw new VirtualStop("ambiguous-id", "Повторный ID / reagent data не поддерживается: " + row.Prototype);
            if (!rules.Reagents.ContainsKey(row.Prototype)) throw new VirtualStop("unknown-reagent", "Нет прототипа в снимке SS220: " + row.Prototype);
            Buffer.Add(row.Prototype, ChemistryVirtual.Cents(row.Amount));
        }
    }

    public VirtualChemMaster Clone()
    {
        var clone = new VirtualChemMaster(Rules, Buffer.Export(), Capacity / 100m, Profile, _names)
        { Powered = Powered, HasBeaker = HasBeaker, Mode = Mode, Sorting = Sorting, _bufferScroll = _bufferScroll, _inputScroll = _inputScroll };
        foreach (var row in Beaker.Items) clone.Beaker.Add(row.Prototype, row.Amount);
        clone.Beaker.Temperature = Beaker.Temperature;
        return clone;
    }

    public void CheckReady()
    {
        if (!Powered) throw new VirtualStop("power-off", "Нет подтверждённого питания/доступного UI.");
        if (!HasBeaker) throw new VirtualStop("no-beaker", "Нет входной ёмкости.");
        if (Mode != "transfer") throw new VirtualStop("wrong-mode", "Нельзя готовить в режиме «Уничтожить».");
    }

    public void SetSorting(string sorting)
    {
        if (sorting != "none" && sorting != "alphabetical" && sorting != "quantity" && sorting != "latest")
            throw new VirtualStop("invalid-sorting", "Неизвестная сортировка.");
        Sorting = sorting;
    }

    public ChemMasterUiSnapshot Ui()
    {
        IEnumerable<VirtualQuantity> rows = Buffer.Items;
        rows = Sorting switch
        {
            "alphabetical" => rows.OrderBy(x => _names.GetValueOrDefault(x.Prototype, x.Prototype),
                StringComparer.Create(CultureInfo.GetCultureInfo("ru-RU"), false)),
            "quantity" => rows.OrderByDescending(x => x.Amount),
            "latest" => rows.Reverse(),
            _ => rows,
        };
        return new ChemMasterUiSnapshot { Source = "virtual-model", RowOrderValid = true,
            BufferRows = rows.Select((x, i) => new ChemMasterUiRow(i, x.Prototype)).ToList(),
            InputRows = Beaker.Items.Select((x, i) => new ChemMasterUiRow(i, x.Prototype)).ToList() };
    }

    public string Fingerprint() => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
    {
        Powered, HasBeaker, Mode, Sorting, Capacity, Buffer = Buffer.Items, Beaker = Beaker.Items, Beaker.Temperature,
        _bufferScroll, _inputScroll
    }))));

    public VirtualCommand Prepare(string id, string dose, bool fromBuffer)
    {
        CheckReady();
        if (!ChemCalibration.Doses.Contains(dose)) throw new VirtualStop("invalid-dose", "Нет такой кнопки дозировки.");
        var ui = Ui();
        var rows = fromBuffer ? ui.BufferRows : ui.InputRows;
        var row = rows.SingleOrDefault(x => x.Prototype == id) ?? throw new VirtualStop("missing-row", "Нет строки " + id);
        int first = fromBuffer ? _bufferScroll : _inputScroll;
        bool scroll = false;
        CalibrationPoint? point = null;
        if (Profile != null)
        {
            string list = fromBuffer ? "buffer" : "input";
            try { point = ChemCalibration.PreviewVirtualReagentPoint(Profile, ui, list, id, dose, first, Profile.Regions["frame"]); }
            catch (Exception e) when (e is ArgumentException || e is InvalidOperationException)
            {
                // Only a virtual alignment; a future live driver must actually scroll and observe again.
                first = row.RowIndex;
                scroll = true;
                point = ChemCalibration.PreviewVirtualReagentPoint(Profile, ui, list, id, dose, first, Profile.Regions["frame"]);
            }
        }
        return new VirtualCommand(Fingerprint(), id, dose, fromBuffer, row.RowIndex, first, scroll, point);
    }

    public VirtualAction Apply(VirtualCommand command)
    {
        if (Fingerprint() != command.ExpectedState)
            throw new VirtualStop("state-changed", "Состав/режим/строки изменились после проверки. Остановка и новый план; повторного клика нет.");
        CheckReady();
        if (!ChemCalibration.Doses.Contains(command.Dose)) throw new VirtualStop("invalid-dose", "Неверная кнопка.");
        var source = command.FromBuffer ? Buffer : Beaker;
        var rows = command.FromBuffer ? Ui().BufferRows : Ui().InputRows;
        if (command.RowIndex < 0 || command.RowIndex >= rows.Count || rows[command.RowIndex].Prototype != command.Prototype)
            throw new VirtualStop("stale-row", "Строка больше не соответствует ID.");
        int wanted = command.Dose == "all" ? source.Get(command.Prototype) : int.Parse(command.Dose, CultureInfo.InvariantCulture) * 100;
        if (command.FromBuffer) wanted = Math.Min(wanted, Math.Max(0, Capacity - Beaker.Volume));
        int taken = source.Remove(command.Prototype, wanted, command.FromBuffer);
        var reactions = new List<string>();
        if (command.FromBuffer)
        {
            Beaker.Add(command.Prototype, taken);
            if (taken > 0) React(reactions);
        }
        else
        {
            if (taken > 0) React(reactions);
            // Direct AddReagent, not UpdateChemicals(buffer): UI transfers do NOT mix the buffer.
            Buffer.Add(command.Prototype, taken);
        }
        if (command.FromBuffer) _bufferScroll = command.FirstVisibleRow; else _inputScroll = command.FirstVisibleRow;
        return new VirtualAction(command.Prototype, command.Dose, command.FromBuffer, command.RowIndex,
            command.FirstVisibleRow, command.ScrollRequired, command.Point, taken, Buffer.Export(), Beaker.Export(), Beaker.Temperature, reactions);
    }

    private float HeatCapacity()
    {
        // Keep single-precision accumulation in the current solution order, like SS14.
        float heat = 0;
        foreach (var row in Beaker.Items)
        {
            if (!Rules.Reagents.TryGetValue(row.Prototype, out var data))
                throw new VirtualStop("unknown-heat", "Неизвестна теплоёмкость " + row.Prototype);
            heat += row.Amount / 100f * data.SpecificHeat;
        }
        return heat;
    }

    private void React(List<string> applied)
    {
        var ordered = Rules.Reactions.OrderByDescending(x => x.Priority).ThenBy(x => x.Outputs.Count).ThenBy(x => x.Id, StringComparer.Ordinal);
        for (int iteration = 0; iteration < 20; iteration++)
        {
            GameReaction? reaction = null;
            int repeats = 0;
            foreach (var candidate in ordered)
            {
                if (candidate.MixerCategories.Count != 0 || Beaker.Temperature < candidate.MinTemperature ||
                    (candidate.MaxTemperature != null && Beaker.Temperature > candidate.MaxTemperature)) continue;
                long units = int.MaxValue;
                bool possible = true;
                foreach (var input in candidate.Inputs)
                {
                    int have = Beaker.Get(input.Prototype), coefficient = ChemistryVirtual.Cents(input.Amount);
                    if (coefficient <= 0) throw new VirtualStop("invalid-rules", "Нулевой коэффициент реакции.");
                    if (have <= 0 || (input.Catalyst && candidate.Quantized && have < coefficient)) { possible = false; break; }
                    if (!input.Catalyst) units = Math.Min(units, 100L * have / coefficient);
                }
                if (candidate.Quantized) units = units / 100 * 100;
                if (!possible || units <= 0) continue;
                if (units == int.MaxValue) throw new VirtualStop("invalid-rules", "Реакция без расходуемых веществ.");
                reaction = candidate;
                repeats = checked((int)units);
                break;
            }
            if (reaction == null)
            {
                if (Beaker.Volume > Capacity) throw new VirtualStop("overflow", "Реакция переполнила ёмкость; разлив не моделируется.");
                return;
            }
            if (reaction.HasEffects) throw new VirtualStop("unsupported-reaction", reaction.Id + ": сработал эффект (газ/взрыв/иное), которого нет в модели.");
            float energy = reaction.ConserveEnergy ? HeatCapacity() * Beaker.Temperature : 0;
            foreach (var input in reaction.Inputs.Where(x => !x.Catalyst))
                Beaker.Remove(input.Prototype, checked((int)((long)ChemistryVirtual.Cents(input.Amount) * repeats / 100)), false);
            foreach (var output in reaction.Outputs)
                Beaker.Add(output.Prototype, checked((int)((long)ChemistryVirtual.Cents(output.Amount) * repeats / 100)));
            if (reaction.ConserveEnergy)
            {
                float heat = HeatCapacity();
                if (heat > 0) Beaker.Temperature = energy / heat;
            }
            applied.Add(reaction.Id);
        }
        throw new VirtualStop("reaction-limit", "Не достигнута стабильная смесь за 20 реакций.");
    }
}

internal sealed class GameChemistryRules
{
    public int SchemaVersion { get; set; }
    public string Revision { get; set; } = "";
    public List<GameReaction> Reactions { get; set; } = new();
    public Dictionary<string, GameReagent> Reagents { get; set; } = new(StringComparer.Ordinal);
}
internal sealed class GameReagent { public float SpecificHeat { get; set; } = 1; }
internal sealed class GameReaction
{
    public string Id { get; set; } = "";
    public string Source { get; set; } = "";
    public int Priority { get; set; }
    public float MinTemperature { get; set; }
    public float? MaxTemperature { get; set; }
    public bool ConserveEnergy { get; set; } = true;
    public bool Quantized { get; set; }
    public bool HasEffects { get; set; }
    public List<string> MixerCategories { get; set; } = new();
    public List<GameReactant> Inputs { get; set; } = new();
    public List<VirtualReagent> Outputs { get; set; } = new();
}
internal sealed class GameReactant
{
    public string Prototype { get; set; } = "";
    public decimal Amount { get; set; }
    public bool Catalyst { get; set; }
}
