using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Ss14.Chemistry;

internal static class ChemistryVirtualTests
{
    private sealed record Result(string Group, string Name, bool Passed, string? Error);
    private static readonly List<Result> Results = new();
    private static readonly List<object> Examples = new();
    private static GameChemistryRules Rules = null!;
    private static CalibrationProfile Profile = null!;
    private static Dictionary<string, string> Names = null!;

    public static int Main(string[] args)
    {
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ru-RU");
        string root = args[0], calibration = Path.Combine(root, "tests", "fixtures", "chemmaster-input.test.json");
        byte[] calibrationHash = SHA256.HashData(File.ReadAllBytes(calibration));
        var watch = Stopwatch.StartNew();
        Rules = ChemistryVirtual.LoadRules();
        Profile = ChemistryVirtual.ReadCalibration(calibration);
        Names = ChemistryPlanning.ChemicalNames();
        CalibrationCases();
        TransferCases();
        ProductionCases();
        SelectedMedicineCases(root);
        InventoryMatrix();
        RandomizedBicaridine();
        GuardCases();
        Case("calibration", "Фиксированная тестовая разметка не изменилась", () =>
            Assert(calibrationHash.SequenceEqual(SHA256.HashData(File.ReadAllBytes(calibration))), "Изменена калибровка"));

        string directory = Path.Combine(root, ".test-results");
        Directory.CreateDirectory(directory);
        int failed = Results.Count(x => !x.Passed);
        var report = new { schemaVersion = 1, offlineOnly = true, gameRevision = Rules.Revision,
            reactionCount = Rules.Reactions.Count, reagentCount = Rules.Reagents.Count,
            calibrationSha256 = Convert.ToHexString(calibrationHash), total = Results.Count, failed,
            elapsedSeconds = watch.Elapsed.TotalSeconds, cases = Results, examples = Examples };
        File.WriteAllText(Path.Combine(directory, "chemistry-virtual-report.json"), JsonSerializer.Serialize(report, ChemistryVirtual.Json));
        var md = new StringBuilder("# Офлайн-тесты Химмастера\n\n");
        md.AppendLine($"Результат: **{Results.Count - failed}/{Results.Count} пройдено**, ошибок: {failed}.");
        md.AppendLine($"\nSS220: `{Rules.Revision}`. Правил реакций: {Rules.Reactions.Count}. Прототипов веществ: {Rules.Reagents.Count}.");
        md.AppendLine("\nИгра и память игрового процесса не использовались. Калибровка проверена структурно и не изменена.");
        md.AppendLine("\n| Группа | Пройдено | Всего |\n|---|---:|---:|");
        foreach (var group in Results.GroupBy(x => x.Group))
            md.AppendLine($"| {group.Key} | {group.Count(x => x.Passed)} | {group.Count()} |");
        md.AppendLine("\n## Что это проверяет\n\nНаличие и нехватку исходников/промежуточных лекарств, целые партии, повторные заказы, возврат в буфер, четыре сортировки, перестановку строк, вместимость, реакции между кликами, катализаторы и остановку при изменении состояния.");
        md.AppendLine("\n## Граница достоверности\n\nЭто независимая модель по публичным исходникам, не запуск игрового сервера. Эффекты, внешнее оборудование, фасовка и drag-and-drop не реализованы. Проверка координат не заменяет проверку живых кнопок и фактической прокрутки. Версия сервера и runtime-изменения прототипов не проверялись.");
        md.AppendLine("\n## Все сценарии\n\n| Результат | Группа | Сценарий |\n|---|---|---|");
        foreach (var result in Results)
            md.AppendLine($"| {(result.Passed ? "PASS" : "FAIL")} | {result.Group} | {result.Name.Replace("|", "/")} {(result.Error ?? "").Replace("|", "/").Replace('\n', ' ')} |");
        File.WriteAllText(Path.Combine(directory, "chemistry-virtual-report.md"), md.ToString(), new UTF8Encoding(false));
        Console.WriteLine($"Virtual chemistry: {Results.Count - failed}/{Results.Count} passed, {failed} failed, {watch.Elapsed.TotalSeconds:F1}s. No game access.");
        foreach (var group in Results.GroupBy(x => x.Group)) Console.WriteLine($"  {group.Key}: {group.Count(x => x.Passed)}/{group.Count()}");
        Console.WriteLine("Report: " + Path.Combine(directory, "chemistry-virtual-report.md"));
        return failed == 0 ? 0 : 1;
    }

    private static void Case(string group, string name, Action action)
    {
        try { action(); Results.Add(new Result(group, name, true, null)); }
        catch (Exception e) { Results.Add(new Result(group, name, false, e.Message)); Console.WriteLine("FAIL " + name + ": " + e.Message); }
    }
    private static void Assert(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static void Equal<T>(T actual, T expected, string message) => Assert(EqualityComparer<T>.Default.Equals(actual, expected), $"{message}: expected {expected}, got {actual}");
    private static void Near(double actual, double expected, string message) =>
        Assert(Math.Abs(actual - expected) <= 0.000001, $"{message}: expected {expected}, got {actual}");
    private static List<VirtualReagent> Stock(params (string Id, decimal Amount)[] values) => values.Select(x => new VirtualReagent(x.Id, x.Amount)).ToList();
    private static VirtualChemMaster Machine(List<VirtualReagent> stock, decimal capacity = 100) => new(Rules, stock, capacity, Profile, Names);
    private static VirtualChemMaster Raw(params (string Id, decimal Amount)[] values) => Machine(Stock(values));
    private static VirtualJobResult Run(VirtualChemMaster machine, string request, string mode = "ensure") => ChemistryVirtual.Execute(machine, new VirtualJob { Request = request, Mode = mode });
    private static VirtualJobResult Success(VirtualChemMaster machine, string request, string mode = "ensure")
    {
        var result = Run(machine, request, mode);
        Equal(result.Status, "completed", result.Detail);
        Equal(machine.Beaker.Volume, 0, "Мензурка должна быть опустошена");
        foreach (var action in result.Actions)
        {
            Assert(action.AmountHundredths > 0, "Пустой клик");
            Assert(action.Point != null, "Нет калиброванной точки");
            Assert(Profile.Regions[action.FromBuffer ? "bufferViewport" : "inputViewport"].Contains(action.Point!), "Клик вне viewport");
            Assert(action.BeakerAfter.Sum(x => x.Amount) <= machine.Capacity / 100m, "Переполнение в trace");
        }
        return result;
    }
    private static void Blocked(VirtualChemMaster machine, string request, string status)
    {
        string before = machine.Fingerprint();
        var result = Run(machine, request);
        Equal(result.Status, status, result.Detail);
        Equal(result.Actions.Count, 0, "Отказ должен быть до первого действия");
        Equal(machine.Fingerprint(), before, "Отказ изменил исходное состояние");
    }
    private static VirtualAction Click(VirtualChemMaster machine, string id, string dose, bool buffer = true) => machine.Apply(machine.Prepare(id, dose, buffer));
    private static void Throws(string code, Action action)
    {
        try { action(); }
        catch (VirtualStop e) { Equal(e.Code, code, "Тип остановки"); return; }
        throw new Exception("Ожидалась остановка " + code);
    }

    private static void CalibrationCases()
    {
        Case("calibration", "Полная фиксированная тестовая разметка 1000×900", () =>
        {
            Equal(ChemCalibration.Validate(Profile).Count, 0, "Неверная разметка");
            Equal(Profile.ImageWidth, 1000, "Ширина"); Equal(Profile.ImageHeight, 900, "Высота");
            Equal(Profile.Points.Count, 29, "Число точек");
        });
        Case("calibration", "Виртуальные строки не принимаются за живое UI", () =>
        {
            var machine = Raw(("Water", 1));
            bool refused = false;
            try { ChemCalibration.PreviewReagentPoint(Profile, machine.Ui(), "buffer", "Water", "1", 0, Profile.Regions["frame"]); }
            catch (InvalidOperationException) { refused = true; }
            Assert(refused, "Нарушена граница live/virtual");
        });
        foreach (string dose in ChemCalibration.Doses)
            Case("calibration", "Все дозировки первой строки: " + dose, () =>
            {
                var machine = Raw(("Water", 100));
                var command = machine.Prepare("Water", dose, true);
                Near(command.Point!.X, Profile.Points["buffer." + dose].X, "X без интерполяции");
                Near(command.Point.Y, Profile.Points["buffer." + dose].Y, "Y первой строки");
                Assert(!command.ScrollRequired, "Лишняя прокрутка");
            });
        Case("calibration", "Вещество на 40-й строке требует прокрутки", () =>
        {
            var stock = Rules.Reagents.Keys.Take(40).Select(id => new VirtualReagent(id, 1)).ToList();
            var machine = Machine(stock);
            var command = machine.Prepare(stock[^1].Prototype, "all", true);
            Equal(command.RowIndex, 39, "Индекс строки");
            Assert(command.ScrollRequired, "Скрытая строка не распознана");
            Equal(command.FirstVisibleRow, 39, "Виртуальное выравнивание прокрутки");
        });
        Case("calibration", "Обрезанная нижняя строка не используется", () =>
        {
            var machine = Machine(Rules.Reagents.Keys.Take(12).Select(id => new VirtualReagent(id, 1)).ToList());
            bool rejected = false;
            try { ChemCalibration.PreviewVirtualReagentPoint(Profile, machine.Ui(), "buffer", machine.Buffer.Items[7].Prototype, "1", 0, Profile.Regions["frame"]); }
            catch (InvalidOperationException) { rejected = true; }
            Assert(rejected, "Использована обрезанная строка");
        });
    }

    private static void TransferCases()
    {
        Case("source", "Зафиксированные игровые данные: 345 реакций, 453 вещества", () =>
        {
            Equal(Rules.Revision, "86d0f7bffb5f3f4d3ee7bef3b9080c2e37b7ec03", "Commit");
            Equal(Rules.Reactions.Count, 345, "Реакции"); Equal(Rules.Reagents.Count, 453, "Вещества");
            Equal(Rules.Reactions.Single(x => x.Id == "Inaprovaline").Outputs.Single().Amount, 3m, "Выход инапровалина");
            Assert(Rules.Reactions.Single(x => x.Id == "Dexalin").Inputs.Single(x => x.Prototype == "Plasma").Catalyst, "Катализатор");
        });
        foreach (string sort in new[] { "none", "alphabetical", "quantity", "latest" })
            Case("rows", "Порядок и стабильные равные количества: " + sort, () =>
            {
                var machine = Raw(("Carbon", 5), ("Nitrogen", 10), ("Oxygen", 10));
                machine.SetSorting(sort);
                string expected = sort switch { "none" => "Carbon,Nitrogen,Oxygen", "latest" => "Oxygen,Nitrogen,Carbon", _ => "Nitrogen,Oxygen,Carbon" };
                Equal(string.Join(",", machine.Ui().BufferRows.Select(x => x.Prototype)), expected, "Порядок UI");
                Equal(machine.Buffer.Items[0].Prototype, "Carbon", "Сортировка не меняет raw");
            });
        Case("rows", "Удаление в буфере сохраняет порядок остальных", () =>
        {
            var machine = Raw(("Iron", 1), ("Copper", 2), ("Water", 3));
            Click(machine, "Iron", "all");
            Equal(string.Join(",", machine.Buffer.Items.Select(x => x.Prototype)), "Copper,Water", "preserveOrder");
            Click(machine, "Iron", "all", false);
            Equal(string.Join(",", machine.Buffer.Items.Select(x => x.Prototype)), "Copper,Water,Iron", "Повторное добавление в конец");
        });
        Case("rows", "Удаление из мензурки переносит последнюю строку на место первой", () =>
        {
            var machine = Raw();
            machine.Beaker.Add("Iron", 100); machine.Beaker.Add("Copper", 200); machine.Beaker.Add("Water", 300);
            Click(machine, "Iron", "all", false);
            Equal(string.Join(",", machine.Beaker.Items.Select(x => x.Prototype)), "Water,Copper", "RemoveSwap");
            Click(machine, "Water", "all", false); Click(machine, "Copper", "all", false);
            Equal(machine.Beaker.Volume, 0, "Вернуть всё по свежим ID");
            Equal(machine.Buffer.Volume, 600, "Ничего не потеряно");
        });
        Case("rows", "Пополнение существующей строки не перемещает её в конец", () =>
        {
            var machine = Raw(("Iron", 2), ("Copper", 3));
            machine.Beaker.Add("Iron", 100);
            Click(machine, "Iron", "all", false);
            Equal(machine.Buffer.Items[0].Prototype, "Iron", "Порядок при объединении"); Equal(machine.Buffer.Get("Iron"), 300, "Объединённый объём");
        });
        Case("rows", "Сортировка latest — обратный raw, не время последнего пополнения", () =>
        {
            var machine = Raw(("Iron", 2), ("Copper", 3)); machine.SetSorting("latest");
            machine.Beaker.Add("Iron", 100); Click(machine, "Iron", "all", false);
            Equal(machine.Ui().BufferRows[0].Prototype, "Copper", "Latest не поднимает пополненную старую строку");
        });
        Case("transfer", "«Всё» относится только к одному веществу", () =>
        {
            var machine = Raw(("Iron", 2), ("Copper", 3)); Click(machine, "Iron", "all");
            Equal(machine.Buffer.Get("Copper"), 300, "Соседний реагент"); Equal(machine.Beaker.Volume, 200, "Вход");
        });
        Case("transfer", "Доза ограничена свободным объёмом", () =>
        {
            var machine = Machine(Stock(("Water", 10)), 3); var action = Click(machine, "Water", "5");
            Equal(action.AmountHundredths, 300, "Clamp вместимости"); Equal(machine.Buffer.Get("Water"), 700, "Остаток");
        });
        Case("transfer", "Доза ограничена наличием реагента", () =>
        {
            var machine = Raw(("Water", 0.37m)); var action = Click(machine, "Water", "100");
            Equal(action.AmountHundredths, 37, "Clamp исходника");
        });
        Case("transfer", "Точная дробь через «Всё» и обратно", () =>
        {
            var machine = Raw(("Water", 0.37m)); Click(machine, "Water", "all"); Click(machine, "Water", "all", false);
            Equal(machine.Buffer.Get("Water"), 37, "Сотые доли");
        });
        Case("transfer", "Буфер не смешивается при возврате через кнопки", () =>
        {
            var machine = Raw(("Dylovene", 5)); machine.Beaker.Add("Inaprovaline", 500); Click(machine, "Inaprovaline", "all", false);
            Equal(machine.Buffer.Get("Tricordrazine"), 0, "Не должно быть реакции в буфере"); Equal(machine.Buffer.Items.Count, 2, "Два реагента");
        });
        Case("reactions", "Смесь реагирует сразу после добавления второй части", () =>
        {
            var machine = Raw(("Dylovene", 5), ("Inaprovaline", 5)); Click(machine, "Dylovene", "all"); var action = Click(machine, "Inaprovaline", "all");
            Equal(machine.Beaker.Get("Tricordrazine"), 1000, "Реакция в мензурке"); Assert(action.Reactions.Contains("Tricordrazine"), "Нет реакции в trace");
        });
        Case("reactions", "Наивные четыре клика углерода портят инапровалин", () =>
        {
            var machine = Raw(("Oxygen", 4), ("Sugar", 4), ("Carbon", 10));
            Click(machine, "Oxygen", "all"); Click(machine, "Sugar", "all");
            for (int i = 0; i < 4; i++) Click(machine, "Carbon", "1");
            Assert(machine.Beaker.Get("Bicaridine") > 0, "Контрпример перестал ловить побочную реакцию");
            Assert(machine.Beaker.Get("Inaprovaline") < 1200, "Неверно выдан успех 12u");
            Examples.Add(new { name = "Наивный порядок, НЕ использовать", beaker = machine.Beaker.Export() });
        });
        Case("reactions", "Несовместимые brute-лекарства превращаются в Razorium", () =>
        {
            var machine = Raw(("Bicaridine", 1), ("Lacerinol", 1)); Click(machine, "Bicaridine", "all"); Click(machine, "Lacerinol", "all");
            Equal(machine.Beaker.Get("Razorium"), 100, "Конфликт медикаментов");
        });
        Case("reactions", "Непрерывный катализатор: достаточно 0.01u плазмы", () =>
        {
            var machine = Raw(("Oxygen", 2), ("Plasma", 0.01m)); Click(machine, "Oxygen", "all"); Click(machine, "Plasma", "all");
            Equal(machine.Beaker.Get("Dexalin"), 300, "Выход декcалина"); Equal(machine.Beaker.Get("Plasma"), 1, "Катализатор не расходуется");
        });
        Case("reactions", "FixedPoint2: 0.01u кислорода не хватает на дробь 0.005 реакции", () =>
        {
            var machine = Raw(("Oxygen", 0.01m), ("Plasma", 1)); Click(machine, "Oxygen", "all"); Click(machine, "Plasma", "all");
            Equal(machine.Beaker.Get("Dexalin"), 0, "Нельзя округлять реакцию вверх"); Equal(machine.Beaker.Get("Oxygen"), 1, "След кислорода сохранён");
        });
        Case("reactions", "Минимальная температура включительна в серверной логике", () =>
        {
            var cold = Raw(("Hydrogen", 1), ("Oxygen", 1)); cold.Beaker.Temperature = 309.99f;
            Click(cold, "Oxygen", "all"); Click(cold, "Hydrogen", "all"); Equal(cold.Beaker.Get("Hydroxide"), 0, "Ниже minTemp");
            var warm = Raw(("Hydrogen", 1), ("Oxygen", 1)); warm.Beaker.Temperature = 310;
            Click(warm, "Oxygen", "all"); Click(warm, "Hydrogen", "all"); Equal(warm.Beaker.Get("Hydroxide"), 200, "Ровно minTemp");
        });
    }

    private static void ProductionCases()
    {
        Case("production", "Готовое лекарство — ни одного клика", () =>
        {
            var machine = Raw(("Bicaridine", 20)); var result = Success(machine, "Bicaridine=20"); Equal(result.Actions.Count, 0, "Готовый запас");
        });
        Case("production", "Частичный запас — готовить только недостающее", () =>
        {
            var machine = Raw(("Bicaridine", 9), ("Inaprovaline", 10), ("Carbon", 10)); Success(machine, "Bicaridine=20");
            Equal(machine.Buffer.Get("Bicaridine"), 2100, "Округлённая партия 12u"); Equal(machine.Buffer.Get("Carbon"), 400, "Взять только 6u");
            Equal(machine.Buffer.Items[0].Prototype, "Bicaridine", "Старая строка осталась на месте");
        });
        Case("production", "Два готовых промежуточных препарата", () =>
        {
            var machine = Raw(("Dylovene", 5), ("Inaprovaline", 5)); var result = Success(machine, "Tricordrazine=10");
            Equal(result.Plan!.Steps.Count, 1, "Нельзя пересинтезировать готовые промежуточные"); Equal(machine.Buffer.Get("Tricordrazine"), 1000, "Результат");
        });
        Case("production", "Инапровалин 12u — безопасные малые партии вместо побочного бикаридина", () =>
        {
            var machine = Raw(("Oxygen", 100), ("Sugar", 100), ("Carbon", 100)); var result = Success(machine, "Inaprovaline=12");
            Equal(machine.Buffer.Get("Inaprovaline"), 1200, "Чистый итог"); Equal(machine.Buffer.Get("Bicaridine"), 0, "Нет побочного бикаридина");
            Equal(machine.Buffer.Get("Oxygen"), 9600, "Ровно 4u исходника");
            Assert(result.Actions.Count(x => x.Reactions.Contains("Inaprovaline")) >= 4, "Нужны малые партии");
            Examples.Add(new { name = "Исправленная последовательность инапровалина", result });
        });
        Case("production", "Округление промежуточных партий 5u → 6u и возврат остатков", () =>
        {
            var machine = Elements(); Success(machine, "Tricordrazine=10");
            Equal(machine.Buffer.Get("Tricordrazine"), 1000, "Цель"); Equal(machine.Buffer.Get("Dylovene"), 100, "Остаток диловена"); Equal(machine.Buffer.Get("Inaprovaline"), 100, "Остаток инапровалина");
        });
        Case("production", "Резерв целей: диловен нельзя целиком отдать в трикордразин", () =>
        {
            var machine = Elements(); machine.Buffer.Add("Dylovene", 1000); machine.Buffer.Add("Inaprovaline", 500);
            Success(machine, "Dylovene=10;Tricordrazine=10");
            Assert(machine.Buffer.Get("Dylovene") >= 1000 && machine.Buffer.Get("Tricordrazine") >= 1000, "Одна цель съела другую");
        });
        Case("production", "Повторяющиеся цели суммируются", () =>
        {
            var machine = Elements(); machine.Buffer.Add("Dylovene", 600); Success(machine, "Dylovene=5;Dylovene=5");
            Assert(machine.Buffer.Get("Dylovene") >= 1000, "Потеряна повторная цель");
        });
        Case("production", "Повторяющиеся малые цели округляются одной общей партией", () =>
        {
            var machine = Raw(("Potassium", 1), ("Silicon", 1), ("Nitrogen", 1));
            var result = Success(machine, "Dylovene=1;Dylovene=1");
            Equal(result.Plan!.Requested.Count, 1, "Повторная цель должна быть агрегирована");
            Equal(result.Plan.Requested[0].Amount, 2m, "Суммарная цель");
            Equal(result.Plan.Steps.Single(x => x.Prototype == "Dylovene").TargetAmount, 3m, "Одна целая реакция");
            Equal(machine.Buffer.Get("Dylovene"), 300, "Одна партия должна покрыть обе цели");
        });
        Case("production", "Пять последовательных заказов с пополнением буфера", () =>
        {
            var machine = Elements();
            var results = new[] { Success(machine, "Dylovene=30"), Success(machine, "Tricordrazine=20"),
                Success(machine, "Dylovene=30;Tricordrazine=20"), Success(machine, "Dylovene=30;Tricordrazine=20"),
                Success(machine, "Tricordrazine=10", "make") };
            Equal(results[3].Actions.Count, 0, "ensure должен быть идемпотентен"); Equal(machine.Buffer.Get("Tricordrazine"), 3000, "make добавляет 10u");
            Examples.Add(new { name = "Цикл пяти заказов", results, finalBuffer = machine.Buffer.Export() });
        });
        Case("production", "Сторонний игрок добавил новый реагент между заказами", () =>
        {
            var machine = Elements(); machine.SetSorting("quantity"); Success(machine, "Dylovene=12");
            machine.Buffer.Add("Iron", 30000); machine.Buffer.Remove("Dylovene", 600, true);
            var result = Success(machine, "Dylovene=12"); Equal(machine.Buffer.Get("Dylovene"), 1200, "Свежий дефицит");
            Assert(result.Actions.Count > 0, "Нельзя использовать старый успешный план");
        });
        foreach (int capacity in new[] { 3, 5, 10, 30, 50, 100 })
            Case("capacity", "Бикаридин 120u через ёмкость " + capacity, () =>
            {
                var machine = Machine(Stock(("Inaprovaline", 80), ("Carbon", 80)), capacity);
                Success(machine, "Bicaridine=120"); Equal(machine.Buffer.Get("Bicaridine"), 12000, "Полный заказ");
            });
        Case("catalyst", "Дексалин с расширением объёма и многократным возвратом плазмы", () =>
        {
            var machine = Machine(Stock(("Oxygen", 30), ("Plasma", 1)), 10); var result = Success(machine, "Dexalin=30");
            Equal(machine.Buffer.Get("Dexalin"), 3000, "Выход"); Equal(machine.Buffer.Get("Plasma"), 100, "Возврат катализатора");
            Equal(machine.Buffer.Get("Oxygen"), 1000, "Расход O2"); Assert(result.Actions.Count(x => x.Prototype == "Plasma" && !x.FromBuffer) > 1, "Повторное использование катализатора");
        });
        Case("catalyst", "Зарезервированная цель может быть временным катализатором", () =>
        {
            var machine = Raw(("Oxygen", 2), ("Plasma", 1));
            var result = Success(machine, "Dexalin=3;Plasma=1");
            Equal(result.Plan!.BaseRequirements.Count, 0, "Нельзя требовать вторую нерасходуемую плазму");
            Equal(machine.Buffer.Get("Dexalin"), 300, "Дексалин");
            Equal(machine.Buffer.Get("Plasma"), 100, "Целевой запас плазмы сохранён");
        });
        Case("production", "Готовый нагреваемый препарат не требует повторного нагрева", () =>
        { var machine = Raw(("Arithrazine", 10)); Equal(Success(machine, "Arithrazine=10").Actions.Count, 0, "Готовый арифразин"); });
        Case("production", "Эпинефрин из готовых компонентов", () =>
        {
            var machine = Raw(("Phenol", 10), ("Acetone", 10), ("Chlorine", 10), ("Hydroxide", 10));
            Success(machine, "Epinephrine=20"); Equal(machine.Buffer.Get("Epinephrine"), 2000, "Эпинефрин");
        });
        Case("production", "Несколько лекарств в одном заказе", () =>
        {
            var machine = Elements(); Success(machine, "Bicaridine=20;Kelotane=20;Tricordrazine=10");
            foreach (string id in new[] { "Bicaridine", "Kelotane", "Tricordrazine" }) Assert(machine.Buffer.Get(id) >= 1000, "Не достигнута цель " + id);
        });
        Case("production", "Побочный продукт крови тоже возвращается отдельной строкой", () =>
        {
            var machine = Raw(("Ambuzol", 5), ("ZombieBlood", 15)); Success(machine, "AmbuzolPlus=5");
            Equal(machine.Buffer.Get("AmbuzolPlus"), 500, "Амбузол плюс"); Equal(machine.Buffer.Get("Blood"), 1500, "Побочный выход крови");
            Equal(machine.Buffer.Items.Count, 2, "Оба продукта возвращены без примесей");
        });
        Case("production", "Побочный выход удовлетворяет вторую выбранную цель", () =>
        {
            var machine = Raw(("Ambuzol", 5), ("ZombieBlood", 15));
            var result = Success(machine, "AmbuzolPlus=5;Blood=15");
            Equal(result.Plan!.Steps.Count, 1, "Одна реакция не должна дублироваться для побочного продукта");
            Equal(machine.Buffer.Get("AmbuzolPlus"), 500, "Основной выход");
            Equal(machine.Buffer.Get("Blood"), 1500, "Побочный выход одновременно является целью");
        });
        Case("production", "Доступная безопасная альтернатива предпочтительнее взрывной", () =>
        {
            var machine = Raw(("Bicaridine", 1), ("Lacerinol", 1));
            var result = Success(machine, "Razorium=1");
            var step = result.Plan!.Steps.Single(x => x.Prototype == "Razorium");
            Equal(string.Join(",", step.Inputs.Select(x => x.Prototype).OrderBy(x => x, StringComparer.Ordinal)),
                "Bicaridine,Lacerinol", "Выбран доступный безопасный вариант");
            Equal(machine.Buffer.Get("Razorium"), 100, "Безопасный выход бритвиума");
        });
        Case("production", "Рвотное: выход 2u из 3u исходников", () =>
        {
            var machine = Raw(("Potassium", 5), ("Nitrogen", 5), ("Ammonia", 5)); Success(machine, "Ipecac=10");
            Equal(machine.Buffer.Get("Ipecac"), 1000, "Уменьшение объёма при реакции"); Equal(machine.Buffer.Volume, 1000, "Остаток буфера");
        });
        Case("production", "Лепоразин: готовый ферросилиций и повторное использование катализатора", () =>
        {
            var machine = Raw(("Copper", 5), ("Fersilicite", 5), ("Plasma", 1)); Success(machine, "Leporazine=10");
            Equal(machine.Buffer.Get("Leporazine"), 1000, "Лепоразин"); Equal(machine.Buffer.Get("Plasma"), 100, "Катализатор возвращён");
        });
        Case("reactions", "Эффект реакции воды с калием не выдаётся за обычное смешивание", () =>
        {
            var machine = Raw(("Potassium", 1), ("Water", 1)); Click(machine, "Potassium", "all");
            Throws("unsupported-reaction", () => Click(machine, "Water", "all"));
        });
    }

    private static void SelectedMedicineCases(string root)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "src/ChemMaster/chemistry-selections.json")));
        var ids = document.RootElement.GetProperty("categories").EnumerateArray()
            .SelectMany(x => x.GetProperty("medicines").EnumerateArray().Select(v => v.GetString()!)).Distinct().ToList();
        foreach (string id in ids)
        {
            Case("selected-medicines", id + ": отсутствует и нет исходников", () => Blocked(Raw(), id + "=10", "needs-reagents"));
            if (Rules.Reagents.ContainsKey(id))
                Case("selected-medicines", id + ": уже есть ровно 10u", () =>
                {
                    var machine = Raw((id, 10)); var result = Success(machine, id + "=10");
                    Equal(result.Actions.Count, 0, "Готовый препарат не нужно синтезировать заново"); Equal(machine.Buffer.Get(id), 1000, "Готовый запас не потрачен");
                });
        }
    }

    private static VirtualChemMaster Elements() => Raw(("Silicon", 100), ("Nitrogen", 100), ("Potassium", 100), ("Oxygen", 100), ("Carbon", 100), ("Sugar", 100));

    private static void InventoryMatrix()
    {
        string[] bases = { "Silicon", "Nitrogen", "Potassium", "Oxygen", "Carbon", "Sugar" };
        string[] sorts = { "none", "alphabetical", "quantity", "latest" };
        // Exhaust all 2^6 base-stock combinations and all four intermediate-stock cases.
        // Expected success is derived from reagent availability, not from the planner result.
        for (int intermediate = 0; intermediate < 4; intermediate++)
            for (int mask = 0; mask < 64; mask++)
            {
                int ready = intermediate, present = mask;
                Case("stock-matrix", $"Трикордразин: готовые D/I={ready}, маска шести исходников={present:D2}, sort={sorts[present % 4]}", () =>
                {
                    var stock = new List<VirtualReagent>();
                    for (int bit = 0; bit < 6; bit++) if ((present & (1 << bit)) != 0) stock.Add(new VirtualReagent(bases[bit], 10));
                    if ((ready & 1) != 0) stock.Add(new VirtualReagent("Dylovene", 5));
                    if ((ready & 2) != 0) stock.Add(new VirtualReagent("Inaprovaline", 5));
                    var machine = Machine(stock); machine.SetSorting(sorts[present % 4]);
                    bool enough = ((ready & 1) != 0 || (present & 7) == 7) && ((ready & 2) != 0 || (present & 56) == 56);
                    if (enough) { Success(machine, "Tricordrazine=10"); Equal(machine.Buffer.Get("Tricordrazine"), 1000, "Матрица: выход"); }
                    else Blocked(machine, "Tricordrazine=10", "needs-reagents");
                });
            }
    }

    private static void RandomizedBicaridine()
    {
        var random = new Random(4000);
        string[] sorts = { "none", "alphabetical", "quantity", "latest" };
        int[] capacities = { 2, 3, 10, 30, 50, 100 };
        for (int i = 0; i < 100; i++)
        {
            int number = i, goal = random.Next(1, 100), initial = random.Next(0, 30), capacity = capacities[random.Next(capacities.Length)];
            Case("seed-4000", $"Бикаридин #{number}: цель {goal}, запас {initial}, ёмкость {capacity}, sort={sorts[i % 4]}", () =>
            {
                var stock = Stock(("Inaprovaline", 100), ("Carbon", 100), ("Bicaridine", initial), ("Iron", 7));
                stock = stock.OrderBy(_ => random.Next()).ToList();
                var machine = Machine(stock, capacity); machine.SetSorting(sorts[number % 4]);
                Success(machine, "Bicaridine=" + goal);
                int repeats = Math.Max(0, (int)Math.Ceiling((goal - initial) / 2m));
                Equal(machine.Buffer.Get("Bicaridine"), (initial + 2 * repeats) * 100, "Точный конечный запас");
                Equal(machine.Buffer.Get("Carbon"), (100 - repeats) * 100, "Независимый баланс углерода");
                Equal(machine.Buffer.Get("Inaprovaline"), (100 - repeats) * 100, "Независимый баланс инапровалина");
                Equal(machine.Buffer.Get("Iron"), 700, "Посторонний запас нетронут");
                Equal(Success(machine, "Bicaridine=" + goal).Actions.Count, 0, "Повтор заказа без расхода");
            });
        }
    }

    private static void GuardCases()
    {
        Case("guards", "Не хватает ровно 0.01u", () => Blocked(Raw(("Inaprovaline", 5), ("Carbon", 4.99m)), "Bicaridine=10", "needs-reagents"));
        Case("guards", "Нет ни одного исходника", () => Blocked(Raw(), "Bicaridine=10", "needs-reagents"));
        Case("guards", "Исчезло питание", () => { var m = Elements(); m.Powered = false; Blocked(m, "Dylovene=10", "power-off"); });
        Case("guards", "Нет мензурки", () => { var m = Elements(); m.HasBeaker = false; Blocked(m, "Dylovene=10", "no-beaker"); });
        Case("guards", "Активен режим уничтожения", () => { var m = Elements(); m.Mode = "discard"; Blocked(m, "Dylovene=10", "wrong-mode"); });
        Case("guards", "Грязная входная ёмкость", () => { var m = Elements(); m.Beaker.Add("Water", 1); Blocked(m, "Dylovene=10", "beaker-not-empty"); });
        Case("guards", "Не помещается минимальная партия", () => Blocked(Machine(Stock(("Inaprovaline", 10), ("Carbon", 10)), 1), "Bicaridine=10", "capacity-too-small"));
        Case("manual", "Ингредиенты внешнего этапа остаются в мензурке", () =>
        {
            var machine = Raw(("Hyronalin", 5), ("Hydrogen", 5));
            var result = Run(machine, "Arithrazine=10");
            Equal(result.Status, "completed", result.Detail);
            Assert(result.Detail.Contains("Дальше вручную", StringComparison.Ordinal), "Нет инструкции человеку");
            Equal(machine.Beaker.Get("Hyronalin"), 500, "Гироналин не собран");
            Equal(machine.Beaker.Get("Hydrogen"), 500, "Водород не собран");
        });
        Case("guards", "Неизвестная цель среди известных запрещает частичное выполнение", () => Blocked(Elements(), "Dylovene=10;TypoMedicine=10", "invalid-request"));
        Case("guards", "Отрицательная цель", () => Blocked(Elements(), "Dylovene=-1", "invalid-request"));
        Case("guards", "Нулевая цель", () => Blocked(Elements(), "Dylovene=0", "invalid-request"));
        Case("guards", "Неизвестная категория", () => Blocked(Elements(), "@not-a-category=10", "invalid-request"));
        Case("guards", "Повторные прототипы / неизвестные reagent data", () => Throws("ambiguous-id", () => Raw(("Water", 1), ("Water", 2))));
        Case("guards", "Неизвестный прототип в исходном составе", () => Throws("unknown-reagent", () => Raw(("NoSuchReagent", 1))));
        Case("guards", "Точность меньше 0.01 не округляется молча", () => Throws("invalid-amount", () => Raw(("Water", 0.001m))));
        Case("guards", "Отрицательный исходный объём", () => Throws("invalid-amount", () => Raw(("Water", -1))));
        Case("guards", "Объём вне диапазона FixedPoint2", () => Throws("invalid-amount", () => Raw(("Water", 21474837))));
        Case("guards", "Несуществующая кнопка 2u", () => { var m = Raw(("Water", 10)); Throws("invalid-dose", () => m.Prepare("Water", "2", true)); });
        Case("guards", "Несуществующая строка", () => { var m = Raw(("Water", 10)); Throws("missing-row", () => m.Prepare("Iron", "1", true)); });
        Case("guards", "Та же сумма, но другой состав — остановка", () =>
        {
            var m = Raw(("Water", 10), ("Iron", 10)); var command = m.Prepare("Water", "1", true);
            m.Buffer.Remove("Water", 100, true); m.Buffer.Add("Iron", 100); Equal(m.Buffer.Volume, 2000, "Сумма не изменилась");
            Throws("state-changed", () => m.Apply(command)); Equal(m.Beaker.Volume, 0, "Не нажимать при другом составе");
        });
        Case("guards", "Изменение сортировки после выбора строки", () =>
        { var m = Raw(("Water", 10), ("Iron", 10)); var c = m.Prepare("Water", "1", true); m.SetSorting("latest"); Throws("state-changed", () => m.Apply(c)); });
        Case("guards", "Задержавшееся подтверждение не приводит к повторному клику", () =>
        { var m = Raw(("Water", 10)); var c = m.Prepare("Water", "1", true); m.Apply(c); Throws("state-changed", () => m.Apply(c)); Equal(m.Beaker.Volume, 100, "Нет двойного расхода"); });
        Case("guards", "Изменение состава между preflight и первым действием", () =>
        {
            var m = Raw(("Inaprovaline", 10), ("Carbon", 10));
            var result = ChemistryVirtual.Execute(m, new VirtualJob { Request = "Bicaridine=10" }, (i, machine) => { if (i == 0) machine.Buffer.Add("Iron", 100); });
            Equal(result.Status, "state-changed", "Переиспользован старый план"); Equal(result.Actions.Count, 0, "Первый клик запрещён");
        });
        Case("guards", "Сбой после первого клика сохраняет частичное состояние и останавливает цепочку", () =>
        {
            var m = Raw(("Inaprovaline", 10), ("Carbon", 10));
            var result = ChemistryVirtual.Execute(m, new VirtualJob { Request = "Bicaridine=10" }, (i, machine) => { if (i == 1) machine.Powered = false; });
            Equal(result.Status, "state-changed", "Не остановилось"); Equal(result.Actions.Count, 1, "Неверное число кликов"); Assert(m.Beaker.Volume > 0, "Не должно быть вымышленного отката в игре");
        });
        Case("guards", "Рецепт вики и игрового снимка расходятся", () =>
        {
            var rules = ChemistryVirtual.LoadRules(); rules.Reactions.Single(x => x.Id == "Bicaridine").Outputs[0] = new VirtualReagent("Bicaridine", 3);
            var m = new VirtualChemMaster(rules, Stock(("Inaprovaline", 10), ("Carbon", 10)), 100, Profile, Names);
            Blocked(m, "Bicaridine=10", "recipe-mismatch");
        });
    }
}
