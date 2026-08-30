using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

internal static class ChemistryPlanning
{
    private const decimal DefaultAmount = 10m;
    // The runtime catalog is intentionally pinned on first use. Updating JSON files
    // while a process is running must never change a later replan under an already
    // approved execution sequence.
    private static readonly Lazy<ChemistryData> PinnedData = new(
        ReadDataFromDisk, System.Threading.LazyThreadSafetyMode.ExecutionAndPublication);
    private static readonly JsonSerializerOptions ReadJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };
    private static readonly JsonSerializerOptions WriteJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static int RunList(bool json)
    {
        var data = LoadData();
        var chemicals = data.Catalog.Chemicals.ToDictionary(item => item.Prototype, StringComparer.OrdinalIgnoreCase);
        var unresolved = data.Selections.Unresolved.ToDictionary(item => item.Prototype, StringComparer.OrdinalIgnoreCase);
        var categories = data.Selections.Categories.Select(category => new CategoryOutput(
            category.Id,
            category.Name,
            category.Medicines.Select(prototype =>
            {
                if (chemicals.TryGetValue(prototype, out var chemical))
                {
                    return new MedicineOutput(
                        prototype,
                        chemical.DisplayName,
                        chemical.Recipes.Count,
                        chemical.Recipes.Count == 0 ? "source-only" : "recipe",
                        null);
                }

                unresolved.TryGetValue(prototype, out var missing);
                return new MedicineOutput(
                    prototype,
                    missing?.EnteredName ?? prototype,
                    0,
                    "unresolved",
                    missing?.Reason ?? "Вещество отсутствует в каталоге.");
            }).ToList())).ToList();

        var output = new ChemistryListOutput(
            1,
            data.Catalog.Source.RevisionId,
            data.Catalog.Source.Url,
            categories);

        if (json)
        {
            Console.WriteLine(JsonSerializer.Serialize(output, WriteJsonOptions));
            return 0;
        }

        Console.WriteLine($"Каталог химии SS220, ревизия вики {output.CatalogRevision}");
        Console.WriteLine("Формат плана: --chemistry-plan \"Эпинефрин=20;Трикордразин=10\"\n");
        foreach (var category in categories)
        {
            Console.WriteLine($"@{category.Id} — {category.Name}");
            foreach (var medicine in category.Medicines)
            {
                var status = medicine.Status switch
                {
                    "recipe" => $"{medicine.RecipeCount} вар.",
                    "source-only" => "нет рецепта на вики",
                    _ => "не найдено",
                };
                Console.WriteLine($"  {medicine.DisplayName} [{medicine.Prototype}] — {status}");
            }
        }

        return 0;
    }

    public static int RunPlan(string request, bool json)
    {
        var data = LoadData();
        var planner = new Planner(data, gameRules: ChemistryVirtual.LoadRules());
        var output = planner.Build(request);

        if (json)
        {
            Console.WriteLine(JsonSerializer.Serialize(output, WriteJsonOptions));
            return output.Requested.Count == 0 ? 2 : 0;
        }

        PrintPlan(output);
        return output.Requested.Count == 0 ? 2 : 0;
    }

    // Offline execution uses whole stoichiometric batches, so an intermediate such as
    // Dylovene is made as 6u rather than pretending the UI can dispense 1.666...u.
    internal static ChemistryPlanOutput BuildForSimulation(string request,
        IReadOnlyDictionary<string, decimal> inventory, bool ensureStock = true,
        GameChemistryRules? gameRules = null) =>
        new Planner(LoadData(), inventory, ensureStock, true, gameRules ?? ChemistryVirtual.LoadRules()).Build(request);

    internal static Dictionary<string, string> ChemicalNames() => LoadData().Catalog.Chemicals
        .ToDictionary(x => x.Prototype, x => x.DisplayName, StringComparer.Ordinal);

    internal static string BilingualChemicalName(string prototype, string displayName) =>
        string.IsNullOrWhiteSpace(displayName) ||
        displayName.Equals(prototype, StringComparison.CurrentCultureIgnoreCase)
            ? prototype
            : $"{prototype} ({displayName})";

    public static int RunCheck(
        string request,
        bool json,
        bool interfaceOpen,
        bool snapshotValid,
        int? bufferVolumeHundredths,
        IReadOnlyDictionary<string, int> bufferHundredths,
        string? readError)
    {
        var data = LoadData();
        var inventory = bufferHundredths.ToDictionary(
            item => item.Key,
            item => item.Value / 100m,
            StringComparer.OrdinalIgnoreCase);
        var output = new Planner(data, inventory, gameRules: ChemistryVirtual.LoadRules()).Build(request);
        var names = data.Catalog.Chemicals.ToDictionary(item => item.Prototype, item => item.DisplayName, StringComparer.OrdinalIgnoreCase);
        var buffer = inventory
            .OrderBy(item => names.GetValueOrDefault(item.Key, item.Key), StringComparer.CurrentCultureIgnoreCase)
            .Select(item => new RequirementOutput(item.Key, names.GetValueOrDefault(item.Key, item.Key), item.Value))
            .ToList();
        var check = new ChemistryCheckOutput(
            1,
            interfaceOpen,
            snapshotValid,
            bufferVolumeHundredths == null ? null : bufferVolumeHundredths.Value / 100m,
            buffer,
            output,
            readError);

        if (json)
        {
            Console.WriteLine(JsonSerializer.Serialize(check, WriteJsonOptions));
            return !interfaceOpen || !snapshotValid ? 3 : output.Requested.Count == 0 ? 2 : 0;
        }

        if (!interfaceOpen)
            Console.WriteLine("ChemMaster закрыт: план рассчитан, но содержимое буфера недоступно.\n");
        else if (!snapshotValid)
            Console.WriteLine($"ChemMaster открыт, но State не прочитан: {readError ?? "неизвестная ошибка"}\n");
        else
            Console.WriteLine($"ChemMaster открыт: буфер {(bufferVolumeHundredths ?? 0) / 100m:0.##}, реагентов {buffer.Count}.\n");
        PrintPlan(output);
        return !interfaceOpen || !snapshotValid ? 3 : output.Requested.Count == 0 ? 2 : 0;
    }

    private static void PrintPlan(ChemistryPlanOutput output)
    {
        Console.WriteLine($"План по каталогу вики SS220, ревизия {output.CatalogRevision}");
        Console.WriteLine("Теоретические объёмы; действия в игре выполняются вручную.\n");

        if (output.Requested.Count > 0)
        {
            Console.WriteLine("Цели:");
            foreach (var target in output.Requested)
                Console.WriteLine($"  {Format(target.Amount)} ед. {target.DisplayName} [{target.Prototype}]");
        }

        if (output.Steps.Count > 0)
        {
            Console.WriteLine("\nПорядок приготовления:");
            foreach (var step in output.Steps)
            {
                Console.WriteLine($"  {step.Number}. {Format(step.TargetAmount)} ед. {step.DisplayName} — {step.ActionText}");
                Console.WriteLine("     взять: " + string.Join(", ", step.Inputs.Select(input =>
                    $"{Format(input.Amount)} {input.Name}{(input.Catalyst ? " (катализатор, не расходуется)" : "")}")));
                if (step.MinimumTemperatureKelvinExclusive != null)
                    Console.WriteLine($"     температура: выше {Format(step.MinimumTemperatureKelvinExclusive.Value)} K");
                if (step.MaximumTemperatureKelvinExclusive != null)
                    Console.WriteLine($"     температура: ниже {Format(step.MaximumTemperatureKelvinExclusive.Value)} K");
                if (step.Byproducts.Count > 0)
                    Console.WriteLine("     побочные продукты: " + string.Join(", ", step.Byproducts.Select(item => $"{Format(item.Amount)} {item.Name}")));
                if (step.GasProducts.Count > 0)
                    Console.WriteLine("     газ: " + string.Join(", ", step.GasProducts.Select(item =>
                        $"{Format(item.AmountMoles)} моль {item.Name}")));
                if (step.RequiresExternalApparatus)
                    Console.WriteLine("     требуется оборудование/условие вне простого переливания ChemMaster");
            }
        }

        if (output.InventoryUsed.Count > 0)
        {
            Console.WriteLine("\nБудет использовано из уже загруженного буфера:");
            foreach (var item in output.InventoryUsed)
                Console.WriteLine($"  {Format(item.Amount)} ед. {item.DisplayName} [{item.Prototype}]");
        }

        if (output.BaseRequirements.Count > 0)
        {
            Console.WriteLine("\nЗагрузить или получить из внешнего источника:");
            foreach (var item in output.BaseRequirements)
                Console.WriteLine($"  {Format(item.Amount)} ед. {item.DisplayName} [{item.Prototype}]");
        }

        if (output.Warnings.Count > 0)
        {
            Console.WriteLine("\nОграничения:");
            foreach (var warning in output.Warnings)
                Console.WriteLine($"  - {warning}");
        }
    }

    private static string Format(decimal value) => value.ToString("0.####", CultureInfo.InvariantCulture);

    private static ChemistryData LoadData() => PinnedData.Value;

    private static ChemistryData ReadDataFromDisk()
    {
        var baseDirectory = AppContext.BaseDirectory;
        var catalogPath = Path.Combine(baseDirectory, "chemistry-recipes.json");
        var selectionsPath = Path.Combine(baseDirectory, "chemistry-selections.json");
        if (!File.Exists(catalogPath))
            throw new FileNotFoundException("Не найден каталог chemistry-recipes.json. Запустите build.ps1.", catalogPath);
        if (!File.Exists(selectionsPath))
            throw new FileNotFoundException("Не найден файл chemistry-selections.json. Запустите build.ps1.", selectionsPath);

        var catalog = JsonSerializer.Deserialize<ChemistryCatalog>(File.ReadAllText(catalogPath, Encoding.UTF8), ReadJsonOptions)
                      ?? throw new InvalidDataException("Каталог chemistry-recipes.json пуст или повреждён.");
        var selections = JsonSerializer.Deserialize<ChemistrySelections>(File.ReadAllText(selectionsPath, Encoding.UTF8), ReadJsonOptions)
                         ?? throw new InvalidDataException("Файл chemistry-selections.json пуст или повреждён.");
        if (catalog.SchemaVersion != 1 || selections.SchemaVersion != 1)
            throw new InvalidDataException("Неподдерживаемая версия схемы каталога химии.");
        return new ChemistryData(catalog, selections);
    }

    private sealed class Planner
    {
        private readonly ChemistryData _data;
        private readonly Dictionary<string, Chemical> _chemicals;
        private readonly Dictionary<string, Chemical> _names;
        private readonly Dictionary<string, AliasEntry> _aliases;
        private readonly Dictionary<string, UnresolvedEntry> _unresolved;
        private readonly Dictionary<string, SelectionCategory> _categories;
        private readonly Dictionary<string, decimal> _baseRequirements = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, decimal> _catalystRequirements = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, decimal> _remainingInventory;
        private readonly Dictionary<string, decimal> _availableOutputs = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, decimal> _reservedTargets = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, decimal> _inventoryUsed = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, MutableStep> _steps = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<RequestedTarget> _requested = new();
        private readonly List<string> _targetOrder = new();
        private readonly HashSet<string> _warnings = new(StringComparer.Ordinal);
        private int _sequence;
        private readonly bool _ensureStock;
        private readonly bool _wholeBatches;
        private readonly GameChemistryRules? _gameRules;

        public Planner(ChemistryData data, IReadOnlyDictionary<string, decimal>? inventory = null,
            bool ensureStock = false, bool wholeBatches = false, GameChemistryRules? gameRules = null)
        {
            _ensureStock = ensureStock;
            _wholeBatches = wholeBatches;
            _data = data;
            _gameRules = gameRules;
            _chemicals = data.Catalog.Chemicals.ToDictionary(item => item.Prototype, StringComparer.OrdinalIgnoreCase);
            _names = data.Catalog.Chemicals
                .GroupBy(item => Normalize(item.DisplayName), StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
            _aliases = data.Selections.Aliases
                .GroupBy(item => Normalize(item.EnteredName), StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
            _unresolved = data.Selections.Unresolved
                .SelectMany(item => new[]
                {
                    new KeyValuePair<string, UnresolvedEntry>(Normalize(item.EnteredName), item),
                    new KeyValuePair<string, UnresolvedEntry>(Normalize(item.Prototype), item),
                })
                .GroupBy(item => item.Key, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First().Value, StringComparer.Ordinal);
            _categories = data.Selections.Categories.ToDictionary(item => Normalize(item.Id), StringComparer.Ordinal);
            _remainingInventory = inventory == null
                ? new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
                : inventory
                    .Where(item => item.Value > 0)
                    .GroupBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(group => group.Key, group => group.Sum(item => item.Value), StringComparer.OrdinalIgnoreCase);
        }

        public ChemistryPlanOutput Build(string request)
        {
            foreach (var parsed in ParseRequest(request))
            {
                if (parsed.Name.StartsWith("@", StringComparison.Ordinal))
                {
                    var categoryId = Normalize(parsed.Name[1..]);
                    if (!_categories.TryGetValue(categoryId, out var category))
                    {
                        _warnings.Add($"Неизвестная категория {parsed.Name}.");
                        continue;
                    }

                    foreach (var prototype in category.Medicines)
                        AddTarget(prototype, parsed.Amount);
                    continue;
                }

                AddTarget(parsed.Name, parsed.Amount);
            }

            AggregateTargets();

            // Reserve ALL requested finished medicines before expanding dependencies.
            // Otherwise a later recipe could consume stock promised to an earlier target.
            var deficits = new List<(string Prototype, decimal Amount)>();
            foreach (var target in _requested)
            {
                var deficit = _ensureStock ? TakeInventory(target.Prototype, target.Amount) : target.Amount;
                var reserved = target.Amount - deficit;
                if (reserved > 0)
                    _reservedTargets[target.Prototype] = _reservedTargets.GetValueOrDefault(target.Prototype) + reserved;
                deficits.Add((target.Prototype, deficit));
            }

            foreach (var deficit in deficits)
            {
                var remaining = TakeOutput(deficit.Prototype, deficit.Amount);
                var reservedOutput = deficit.Amount - remaining;
                if (reservedOutput > 0)
                    _reservedTargets[deficit.Prototype] = _reservedTargets.GetValueOrDefault(deficit.Prototype) + reservedOutput;
                Expand(deficit.Prototype, remaining, new HashSet<string>(StringComparer.OrdinalIgnoreCase), false);
                if (remaining > 0)
                    _reservedTargets[deficit.Prototype] = _reservedTargets.GetValueOrDefault(deficit.Prototype) + remaining;
            }

            var orderedSteps = OrderSteps();
            var baseRequirements = _baseRequirements
                .OrderBy(item => DisplayName(item.Key), StringComparer.CurrentCultureIgnoreCase)
                .Select(item => new RequirementOutput(item.Key, DisplayName(item.Key), item.Value))
                .ToList();
            var inventoryUsed = _inventoryUsed
                .OrderBy(item => DisplayName(item.Key), StringComparer.CurrentCultureIgnoreCase)
                .Select(item => new RequirementOutput(item.Key, DisplayName(item.Key), item.Value))
                .ToList();

            if (orderedSteps.SelectMany(step => step.Inputs).Any(item => item.Amount != decimal.Truncate(item.Amount)))
                _warnings.Add("План содержит дробные объёмы; интерфейс ChemMaster может потребовать округления или кнопки «всё».");

            return new ChemistryPlanOutput(
                1,
                _data.Catalog.Source.RevisionId,
                _data.Catalog.Source.Url,
                _requested,
                orderedSteps,
                inventoryUsed,
                baseRequirements,
                _warnings.ToList());
        }

        private void AggregateTargets()
        {
            var aggregated = _requested
                .GroupBy(target => target.Prototype, StringComparer.OrdinalIgnoreCase)
                .Select(group => new RequestedTarget(
                    group.First().Prototype,
                    group.First().DisplayName,
                    group.Sum(target => target.Amount)))
                .ToList();
            _requested.Clear();
            _requested.AddRange(aggregated);
            _targetOrder.Clear();
            _targetOrder.AddRange(aggregated.Select(target => target.Prototype));
        }

        private void AddTarget(string enteredName, decimal amount)
        {
            if (amount <= 0)
            {
                _warnings.Add($"Объём для «{enteredName}» должен быть больше нуля.");
                return;
            }

            if (TryResolveChemical(enteredName, out var chemical))
            {
                _requested.Add(new RequestedTarget(chemical.Prototype, chemical.DisplayName, amount));
                _targetOrder.Add(chemical.Prototype);
                return;
            }

            if (_unresolved.TryGetValue(Normalize(enteredName), out var unresolved))
            {
                _requested.Add(new RequestedTarget(unresolved.Prototype, unresolved.EnteredName, amount));
                _targetOrder.Add(unresolved.Prototype);
                _warnings.Add(unresolved.Reason);
                return;
            }

            _warnings.Add($"Не удалось сопоставить «{enteredName}» с веществом или категорией каталога.");
        }

        private void Expand(string prototype, decimal amount, HashSet<string> stack, bool allowInventory)
        {
            if (amount <= 0)
                return;

            if (allowInventory) amount = TakeAvailable(prototype, amount);
            if (amount <= 0) return;

            if (!_chemicals.TryGetValue(prototype, out var chemical))
            {
                AddBaseRequirement(prototype, amount);
                _warnings.Add($"{prototype}: вещество отсутствует в каталоге, его нужно получить извне.");
                return;
            }

            if (chemical.Recipes.Count == 0)
            {
                AddBaseRequirement(prototype, amount);
                _warnings.Add($"{chemical.DisplayName}: на странице вики нет рецепта синтеза; требуется готовый реагент.");
                return;
            }

            var mixRecipes = chemical.Recipes
                .Where(recipe => recipe.Operation.Equals("mix", StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (mixRecipes.Count == 0)
            {
                var recipe = chemical.Recipes[0];
                AddBaseRequirement(prototype, amount);
                _warnings.Add($"{chemical.DisplayName}: на вики есть только операция «{recipe.ActionText}», которую ChemMaster не выполняет; требуется готовый реагент.");
                return;
            }

            // Do not manufacture a dependency with a recipe that consumes one of its
            // parents. Blood, for example, is a by-product of AmbuzolPlus, whose recipe
            // itself consumes Ambuzol. Expanding that while making Ambuzol creates a
            // bogus Blood -> Ambuzol cycle instead of asking for ready Blood.
            var nonCyclicRecipes = mixRecipes.Where(recipe => recipe.Inputs
                .Where(input => !input.Catalyst)
                .All(input => !stack.Contains(input.Prototype))).ToList();
            if (nonCyclicRecipes.Count == 0)
            {
                AddBaseRequirement(prototype, amount);
                _warnings.Add($"{chemical.DisplayName}: доступный рецепт зависит от уже готовящегося вещества; требуется готовый реагент.");
                return;
            }
            mixRecipes = nonCyclicRecipes;

            if (!stack.Add(prototype))
            {
                AddBaseRequirement(prototype, amount);
                _warnings.Add($"Обнаружен циклический рецепт для {chemical.DisplayName}; это количество оставлено внешним требованием.");
                return;
            }

            try
            {
                if (_steps.TryGetValue(prototype, out var existingStep))
                {
                    ExpandRecipe(chemical, existingStep.Recipe, amount, stack);
                    return;
                }

                var safeRecipes = mixRecipes.Where(recipe => IsAutomationSafe(chemical, recipe)).ToList();
                var candidates = safeRecipes.Count > 0 ? safeRecipes : mixRecipes;
                if (candidates.Count == 1)
                {
                    ExpandRecipe(chemical, candidates[0], amount, stack);
                    return;
                }

                var baseline = CaptureState();
                PlannerState? bestState = null;
                int bestUnsafe = int.MaxValue;
                decimal bestMissing = decimal.MaxValue;
                int bestMissingKinds = int.MaxValue;
                decimal bestInputCost = decimal.MaxValue;
                int bestIndex = int.MaxValue;
                Exception? firstFailure = null;

                for (var index = 0; index < candidates.Count; index++)
                {
                    RestoreState(baseline);
                    var recipe = candidates[index];
                    try
                    {
                        ExpandRecipe(chemical, recipe, amount,
                            new HashSet<string>(stack, StringComparer.OrdinalIgnoreCase));
                    }
                    catch (Exception ex) when (ex is InvalidDataException || ex is OverflowException)
                    {
                        firstFailure ??= ex;
                        continue;
                    }

                    var unsafeSteps = _steps.Values.Count(step => !IsAutomationSafe(step.Chemical, step.Recipe));
                    var missing = _baseRequirements.Values.Sum();
                    var missingKinds = _baseRequirements.Count(item => item.Value > 0);
                    var inputCost = recipe.Inputs.Where(input => !input.Catalyst).Sum(input => input.Amount);
                    var better = bestState == null ||
                                 unsafeSteps < bestUnsafe ||
                                 unsafeSteps == bestUnsafe && missing < bestMissing ||
                                 unsafeSteps == bestUnsafe && missing == bestMissing && missingKinds < bestMissingKinds ||
                                 unsafeSteps == bestUnsafe && missing == bestMissing && missingKinds == bestMissingKinds && inputCost < bestInputCost ||
                                 unsafeSteps == bestUnsafe && missing == bestMissing && missingKinds == bestMissingKinds && inputCost == bestInputCost && index < bestIndex;
                    if (!better) continue;
                    bestState = CaptureState();
                    bestUnsafe = unsafeSteps;
                    bestMissing = missing;
                    bestMissingKinds = missingKinds;
                    bestInputCost = inputCost;
                    bestIndex = index;
                }

                if (bestState == null)
                {
                    RestoreState(baseline);
                    throw firstFailure ?? new InvalidDataException("Не удалось выбрать вариант рецепта для " + prototype);
                }
                RestoreState(bestState);
            }
            finally
            {
                stack.Remove(prototype);
            }
        }

        private void ExpandRecipe(Chemical chemical, Recipe recipe, decimal requiredAmount, HashSet<string> stack)
        {
            var prototype = chemical.Prototype;
            var targetYield = recipe.Outputs.FirstOrDefault(item =>
                item.Prototype.Equals(prototype, StringComparison.OrdinalIgnoreCase))?.Amount ?? 0m;
            if (targetYield <= 0)
            {
                AddBaseRequirement(prototype, requiredAmount);
                _warnings.Add($"Рецепт {chemical.DisplayName} не содержит положительного выхода целевого вещества.");
                return;
            }

            var scale = requiredAmount / targetYield;
            var producedTarget = requiredAmount;
            if (_wholeBatches)
            {
                // At most 100 repeats for hundredth-unit recipe coefficients. Do not
                // silently round each reactant independently and change the chemistry.
                int quantum = Enumerable.Range(1, 100).FirstOrDefault(n => recipe.Inputs
                    .Where(x => !x.Catalyst).All(x => x.Amount * n == decimal.Truncate(x.Amount * n)));
                if (quantum == 0) throw new InvalidDataException("Рецепт нельзя выразить целыми кнопочными дозами: " + prototype);
                scale = decimal.Ceiling(scale / quantum) * quantum;
                producedTarget = scale * targetYield;
            }
            if (!_steps.TryGetValue(prototype, out var step))
            {
                step = new MutableStep(chemical, recipe, _sequence++);
                _steps.Add(prototype, step);
            }
            step.TargetAmount += producedTarget;
            step.Scale += scale;

            foreach (var input in recipe.Inputs)
            {
                if (!input.Catalyst)
                {
                    Expand(input.Prototype, input.Amount * scale, new HashSet<string>(stack, StringComparer.OrdinalIgnoreCase), true);
                    continue;
                }

                var oldAmount = _catalystRequirements.GetValueOrDefault(input.Prototype);
                if (input.Amount > oldAmount)
                {
                    _catalystRequirements[input.Prototype] = input.Amount;
                    var reserved = _reservedTargets.GetValueOrDefault(input.Prototype);
                    var oldUncovered = Math.Max(0m, oldAmount - reserved);
                    var newUncovered = Math.Max(0m, input.Amount - reserved);
                    Expand(input.Prototype, newUncovered - oldUncovered,
                        new HashSet<string>(stack, StringComparer.OrdinalIgnoreCase), true);
                }
            }

            foreach (var output in recipe.Outputs)
            {
                var available = output.Amount * scale;
                if (output.Prototype.Equals(prototype, StringComparison.OrdinalIgnoreCase))
                    available -= requiredAmount;
                AddOutput(output.Prototype, available);
            }

            if (recipe.MinimumTemperatureKelvinExclusive != null || recipe.MaximumTemperatureKelvinExclusive != null)
                _warnings.Add($"{chemical.DisplayName}: перед реакцией требуется отдельно обеспечить температуру.");
            if (recipe.GasProducts.Count > 0)
                _warnings.Add($"{chemical.DisplayName}: реакция выделяет газ ({string.Join(", ", recipe.GasProducts.Select(item => item.Name).Distinct(StringComparer.CurrentCultureIgnoreCase))}).");
        }

        private bool IsAutomationSafe(Chemical chemical, Recipe recipe)
        {
            if (!recipe.Operation.Equals("mix", StringComparison.OrdinalIgnoreCase) ||
                recipe.MinimumTemperatureKelvinExclusive != null ||
                recipe.MaximumTemperatureKelvinExclusive != null)
                return false;
            if (_gameRules == null)
                return true;

            return _gameRules.Reactions.Any(rule => RecipeMatches(chemical, recipe, rule) &&
                (!rule.HasEffects || recipe.GasProducts.Count != 0) && rule.MixerCategories.Count == 0 &&
                rule.MinTemperature <= 293.15f &&
                (rule.MaxTemperature == null || rule.MaxTemperature >= 293.15f));
        }

        private static bool RecipeMatches(Chemical chemical, Recipe recipe, GameReaction rule)
        {
            if (!rule.Outputs.Any(output => output.Prototype == chemical.Prototype) ||
                rule.Inputs.Count != recipe.Inputs.Count || rule.Outputs.Count != recipe.Outputs.Count)
                return false;
            return rule.Inputs.All(input => recipe.Inputs.Any(candidate =>
                       candidate.Prototype == input.Prototype && candidate.Catalyst == input.Catalyst && candidate.Amount == input.Amount)) &&
                   rule.Outputs.All(output => recipe.Outputs.Any(candidate =>
                       candidate.Prototype == output.Prototype && candidate.Amount == output.Amount));
        }

        private List<PlanStepOutput> OrderSteps()
        {
            var result = new List<MutableStep>();
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var visiting = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void Visit(string prototype)
            {
                if (visited.Contains(prototype) || !_steps.TryGetValue(prototype, out var step))
                    return;
                if (!visiting.Add(prototype))
                    return;
                foreach (var input in step.Recipe.Inputs)
                    Visit(input.Prototype);
                visiting.Remove(prototype);
                visited.Add(prototype);
                result.Add(step);
            }

            foreach (var target in _targetOrder)
                Visit(target);
            foreach (var step in _steps.Values.OrderBy(item => item.Sequence))
                Visit(step.Chemical.Prototype);

            return result.Select((step, index) => step.ToOutput(index + 1)).ToList();
        }

        private bool TryResolveChemical(string enteredName, out Chemical chemical)
        {
            if (_chemicals.TryGetValue(enteredName.Trim(), out chemical!))
                return true;
            var normalized = Normalize(enteredName);
            if (_names.TryGetValue(normalized, out chemical!))
                return true;
            if (_aliases.TryGetValue(normalized, out var alias) && _chemicals.TryGetValue(alias.Prototype, out chemical!))
                return true;
            chemical = null!;
            return false;
        }

        private void AddBaseRequirement(string prototype, decimal amount) =>
            _baseRequirements[prototype] = _baseRequirements.GetValueOrDefault(prototype) + amount;

        private void AddOutput(string prototype, decimal amount)
        {
            if (amount <= 0) return;
            _availableOutputs[prototype] = _availableOutputs.GetValueOrDefault(prototype) + amount;
        }

        private decimal TakeOutput(string prototype, decimal amount)
        {
            var used = Math.Min(amount, _availableOutputs.GetValueOrDefault(prototype));
            if (used > 0)
                _availableOutputs[prototype] -= used;
            return amount - used;
        }

        private decimal TakeAvailable(string prototype, decimal amount) =>
            TakeInventory(prototype, TakeOutput(prototype, amount));

        private decimal TakeInventory(string prototype, decimal amount)
        {
            var used = Math.Min(amount, _remainingInventory.GetValueOrDefault(prototype));
            if (used > 0)
            {
                _remainingInventory[prototype] -= used;
                _inventoryUsed[prototype] = _inventoryUsed.GetValueOrDefault(prototype) + used;
            }
            return amount - used;
        }

        private PlannerState CaptureState() => new(
            new Dictionary<string, decimal>(_baseRequirements, StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, decimal>(_catalystRequirements, StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, decimal>(_remainingInventory, StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, decimal>(_availableOutputs, StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, decimal>(_reservedTargets, StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, decimal>(_inventoryUsed, StringComparer.OrdinalIgnoreCase),
            _steps.ToDictionary(item => item.Key, item => item.Value.Clone(), StringComparer.OrdinalIgnoreCase),
            new HashSet<string>(_warnings, StringComparer.Ordinal),
            _sequence);

        private void RestoreState(PlannerState state)
        {
            Copy(state.BaseRequirements, _baseRequirements);
            Copy(state.CatalystRequirements, _catalystRequirements);
            Copy(state.RemainingInventory, _remainingInventory);
            Copy(state.AvailableOutputs, _availableOutputs);
            Copy(state.ReservedTargets, _reservedTargets);
            Copy(state.InventoryUsed, _inventoryUsed);
            _steps.Clear();
            foreach (var item in state.Steps)
                _steps[item.Key] = item.Value.Clone();
            _warnings.Clear();
            foreach (var warning in state.Warnings)
                _warnings.Add(warning);
            _sequence = state.Sequence;
        }

        private static void Copy(Dictionary<string, decimal> source, Dictionary<string, decimal> destination)
        {
            destination.Clear();
            foreach (var item in source)
                destination[item.Key] = item.Value;
        }

        private string DisplayName(string prototype)
        {
            if (_chemicals.TryGetValue(prototype, out var chemical))
                return chemical.DisplayName;
            var unresolved = _data.Selections.Unresolved.FirstOrDefault(item =>
                item.Prototype.Equals(prototype, StringComparison.OrdinalIgnoreCase));
            return unresolved?.EnteredName ?? prototype;
        }

        private static IEnumerable<ParsedRequest> ParseRequest(string request)
        {
            if (string.IsNullOrWhiteSpace(request))
                yield break;

            foreach (var rawPart in request.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var separator = rawPart.LastIndexOf('=');
                if (separator < 0)
                {
                    yield return new ParsedRequest(rawPart.Trim(), DefaultAmount);
                    continue;
                }

                var name = rawPart[..separator].Trim();
                var amountText = rawPart[(separator + 1)..].Trim().Replace(',', '.');
                if (name.Length == 0 || !decimal.TryParse(amountText, NumberStyles.Number, CultureInfo.InvariantCulture, out var amount))
                    throw new ArgumentException($"Неверная цель плана: «{rawPart}». Ожидается Название=объём.");
                yield return new ParsedRequest(name, amount);
            }
        }

        private sealed record PlannerState(
            Dictionary<string, decimal> BaseRequirements,
            Dictionary<string, decimal> CatalystRequirements,
            Dictionary<string, decimal> RemainingInventory,
            Dictionary<string, decimal> AvailableOutputs,
            Dictionary<string, decimal> ReservedTargets,
            Dictionary<string, decimal> InventoryUsed,
            Dictionary<string, MutableStep> Steps,
            HashSet<string> Warnings,
            int Sequence);
    }

    private static string Normalize(string value)
    {
        var normalized = value.Trim().Normalize(NormalizationForm.FormC).ToLowerInvariant().Replace('ё', 'е');
        return string.Join(" ", normalized.Split((char[]?) null, StringSplitOptions.RemoveEmptyEntries));
    }

    private sealed class MutableStep
    {
        public MutableStep(Chemical chemical, Recipe recipe, int sequence)
        {
            Chemical = chemical;
            Recipe = recipe;
            Sequence = sequence;
        }

        public Chemical Chemical { get; }
        public Recipe Recipe { get; }
        public int Sequence { get; }
        public decimal TargetAmount { get; set; }
        public decimal Scale { get; set; }

        public MutableStep Clone() => new(Chemical, Recipe, Sequence)
        {
            TargetAmount = TargetAmount,
            Scale = Scale,
        };

        public PlanStepOutput ToOutput(int number)
        {
            var inputs = Recipe.Inputs.Select(input => new PlanReagentOutput(
                input.Prototype,
                input.Name,
                input.Catalyst ? input.Amount : input.Amount * Scale,
                input.Catalyst)).ToList();
            var byproducts = Recipe.Outputs
                .Where(output => !output.Prototype.Equals(Chemical.Prototype, StringComparison.OrdinalIgnoreCase))
                .Select(output => new PlanReagentOutput(output.Prototype, output.Name, output.Amount * Scale, false))
                .ToList();
            var gasProducts = Recipe.GasProducts
                .GroupBy(output => output.Name, StringComparer.CurrentCultureIgnoreCase)
                .Select(group => new PlanGasOutput(group.First().Name, group.Sum(output => output.AmountMoles) * Scale))
                .ToList();
            return new PlanStepOutput(
                number,
                Chemical.Prototype,
                Chemical.DisplayName,
                TargetAmount,
                Recipe.Operation,
                Recipe.ActionText,
                inputs,
                byproducts,
                gasProducts,
                Recipe.MinimumTemperatureKelvinExclusive,
                Recipe.MaximumTemperatureKelvinExclusive,
                !Recipe.Operation.Equals("mix", StringComparison.OrdinalIgnoreCase) ||
                Recipe.MinimumTemperatureKelvinExclusive != null ||
                Recipe.MaximumTemperatureKelvinExclusive != null);
        }
    }

    private sealed record ChemistryData(ChemistryCatalog Catalog, ChemistrySelections Selections);
    private sealed record ParsedRequest(string Name, decimal Amount);

    private sealed class ChemistryCatalog
    {
        public int SchemaVersion { get; set; }
        public CatalogSource Source { get; set; } = new();
        public List<Chemical> Chemicals { get; set; } = new();
    }

    private sealed class CatalogSource
    {
        public int RevisionId { get; set; }
        public string Url { get; set; } = "";
    }

    private sealed class Chemical
    {
        public string Prototype { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public List<Recipe> Recipes { get; set; } = new();
    }

    private sealed class Recipe
    {
        public string Operation { get; set; } = "";
        public string ActionText { get; set; } = "";
        public decimal? MinimumTemperatureKelvinExclusive { get; set; }
        public decimal? MaximumTemperatureKelvinExclusive { get; set; }
        public List<RecipeReagent> Inputs { get; set; } = new();
        public List<RecipeReagent> Outputs { get; set; } = new();
        public List<GasProduct> GasProducts { get; set; } = new();
    }

    private sealed class GasProduct
    {
        public string Name { get; set; } = "";
        public decimal AmountMoles { get; set; }
    }

    private sealed class RecipeReagent
    {
        public string Prototype { get; set; } = "";
        public string Name { get; set; } = "";
        public decimal Amount { get; set; }
        public bool Catalyst { get; set; }
    }

    private sealed class ChemistrySelections
    {
        public int SchemaVersion { get; set; }
        public List<SelectionCategory> Categories { get; set; } = new();
        public List<AliasEntry> Aliases { get; set; } = new();
        public List<UnresolvedEntry> Unresolved { get; set; } = new();
    }

    private sealed class SelectionCategory
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public List<string> Medicines { get; set; } = new();
    }

    private sealed class AliasEntry
    {
        public string EnteredName { get; set; } = "";
        public string Prototype { get; set; } = "";
    }

    private sealed class UnresolvedEntry
    {
        public string EnteredName { get; set; } = "";
        public string Prototype { get; set; } = "";
        public string Reason { get; set; } = "";
    }

    private sealed record ChemistryListOutput(
        int SchemaVersion,
        int CatalogRevision,
        string SourceUrl,
        List<CategoryOutput> Categories);
    private sealed record CategoryOutput(string Id, string Name, List<MedicineOutput> Medicines);
    private sealed record MedicineOutput(string Prototype, string DisplayName, int RecipeCount, string Status, string? Note);
    internal sealed record ChemistryPlanOutput(
        int SchemaVersion,
        int CatalogRevision,
        string SourceUrl,
        List<RequestedTarget> Requested,
        List<PlanStepOutput> Steps,
        List<RequirementOutput> InventoryUsed,
        List<RequirementOutput> BaseRequirements,
        List<string> Warnings);
    private sealed record ChemistryCheckOutput(
        int SchemaVersion,
        bool InterfaceOpen,
        bool SnapshotValid,
        decimal? BufferVolume,
        List<RequirementOutput> Buffer,
        ChemistryPlanOutput Plan,
        string? Error);
    internal sealed record RequestedTarget(string Prototype, string DisplayName, decimal Amount);
    internal sealed record RequirementOutput(string Prototype, string DisplayName, decimal Amount);
    internal sealed record PlanStepOutput(
        int Number,
        string Prototype,
        string DisplayName,
        decimal TargetAmount,
        string Operation,
        string ActionText,
        List<PlanReagentOutput> Inputs,
        List<PlanReagentOutput> Byproducts,
        List<PlanGasOutput> GasProducts,
        decimal? MinimumTemperatureKelvinExclusive,
        decimal? MaximumTemperatureKelvinExclusive,
        bool RequiresExternalApparatus);
    internal sealed record PlanReagentOutput(string Prototype, string Name, decimal Amount, bool Catalyst);
    internal sealed record PlanGasOutput(string Name, decimal AmountMoles);
}
