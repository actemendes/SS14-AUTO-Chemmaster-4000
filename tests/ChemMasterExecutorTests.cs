using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Ss14.Chemistry;

internal static class ChemMasterExecutorTests
{
    private sealed record Result(string Name, bool Passed, string? Error);
    private static readonly List<Result> Results = new();
    private static GameChemistryRules Rules = null!;
    private static Dictionary<string, string> Names = null!;

    public static async Task<int> Main(string[] args)
    {
        string root = args[0];
        string fixture = Path.Combine(root, "tests", "fixtures", "chemmaster-input.test.json");
        byte[] fixtureHash = SHA256.HashData(File.ReadAllBytes(fixture));
        Rules = ChemistryVirtual.LoadRules();
        Names = ChemistryPlanning.ChemicalNames();

        await Case("Полный рецепт: один клик на одно подтверждение и возврат", FullRecipe);
        await Case("Задержанный State не повторяет клик", DelayedState);
        await Case("Нет изменения State: timeout без повторного клика", NoStateChange);
        await Case("Внешнее изменение буфера ставит на паузу и допускает abort", ExternalChangeAbort);
        await Case("Частичный перенос ставит на паузу и не разрешает replan с грязной мензуркой", PartialTransfer);
        await Case("Движение scroll подтверждается быстрыми reads и полным финальным snapshot", MovingScroll);
        await Case("Некорректный cached hint принудительно переключается на полный scan", InvalidFastHintForcesFullScan);
        await Case("Fast fallback-full hint всё равно требует отдельный финальный full", FastFallbackHintStillRequiresFinalFull);
        await Case("Неоднозначный полный snapshot после fast hint блокирует следующий ввод", FullAmbiguityAfterFastHint);
        await Case("Регресс времени полного snapshot после fast hint блокирует следующий ввод", CausalTimeRegressionAfterFastHint);
        await Case("Изменение ValueTarget после первого stable включает emergency", LateStableTargetDrift);
        await Case("Закрытие панели останавливает до клика", PanelClosed);
        await Case("Неактивное окно: пауза и явное продолжение", InactiveWindow);
        await Case("Ошибка активации SS14 сохраняет паузу без ввода", ResumeActivationFailure);
        await Case("Потеря фокуса после успешной активации повторно ставит на паузу", ResumeActivationDoesNotStick);
        await Case("Ошибка журнала Resume сохраняет паузу без ввода", ResumeJournalFailure);
        await Case("Изменение размера окна требует новой калибровки", WindowResize);
        await Case("Смещение окна допустимо для client-relative координат", ShiftedWindow);
        await Case("Неправильный DPI требует новой калибровки", WrongDpi);
        await Case("Непустая мензурка блокирует запуск", NonEmptyBeaker);
        await Case("Обычная пауза и продолжение не теряют шаг", PauseResume);
        await Case("Ошибка durable Pause сохраняет паузу без ввода", PauseJournalFailure);
        await Case("Отмена запрещает новые клики", Cancel);
        await Case("Аварийная остановка требует явного reset", EmergencyStopReset);
        await Case("Безопасный replan после внешнего изменения только с пустой мензуркой", SafeExternalReplan);
        await Case("JSONL-журнал содержит обязательные поля и валидный JSON", JsonlJournal);
        await Case("Каждая JSONL-запись видна до Dispose журнала", ImmediateJournalVisibility);
        await Case("Видимая движущаяся строка ждёт атомарно стабильный scroll", VisibleMovingScroll);
        await Case("Pause/Resume перечитывает внешний State до клика", PauseResumeExternalMutation);
        await Case("External decision не теряется при синхронном ответе из callback", ExternalDecisionNoLostWakeup);
        await Case("Pause линеаризован с уже начатым commit", PauseCommitRace);
        await Case("Потеря фокуса после click proof блокируется preflight", ClickPreflightFocusLoss);
        await Case("Сдвиг курсора после scroll proof блокируется preflight", ScrollPreflightPointerDrift);
        await Case("Cancel ждёт commit и не отменяет post-click reconcile", CancelCommitRace);
        await Case("Ошибка журнала после клика не отменяет reconcile", JournalFaultAfterClick);
        await Case("Ошибка durable click-sent не допускает следующий ввод после reconcile", JournalFaultAfterClickSent);
        await Case("Ошибка progress callback после клика не отменяет reconcile", ProgressFaultAfterClick);
        await Case("Ошибка telemetry и внешнее изменение после клика не допускают replan-ввод", ClickTelemetryFaultWithExternalChange);
        await Case("Ошибка журнала после wheel hint не отменяет полный reconcile", JournalFaultAfterWheelHint);
        await Case("Ошибка durable scroll-sent не допускает следующий ввод после reconcile", JournalFaultAfterScrollSent);
        await Case("Ошибка progress callback после wheel hint не отменяет полный reconcile", ProgressFaultAfterWheelHint);
        await Case("Ошибка telemetry и внешнее изменение после wheel не допускают replan-ввод", WheelTelemetryFaultWithExternalChange);
        await Case("Неопределённый SendInput включает emergency и reconcile без retry", IndeterminateInput);
        await Case("Устаревший финальный snapshot блокирует commit", StaleFinalSnapshot);
        await Case("Непричинный snapshot с повторным sequence блокирует commit", RepeatedSequenceRejected);
        await Case("Live-like snapshot 2300 мс укладывается в default freshness", LiveCaptureAgeAccepted);
        await Case("Freshness setting принимает только 2000..10000 мс", FreshnessSettingBoundaries);
        await Case("Dispose откладывает source/journal до завершения run", DisposeWhileReading);
        await Case("PreparedScroll отклоняет быстрый Pause→Resume epoch без wheel", PreparedScrollPauseResumeRace);
        await Case("PreparedScroll отклоняет stale scroll snapshot и перечитывает", PreparedScrollStaleUi);
        await Case("PreparedScroll не отправляет wheel при stale panel geometry", PreparedScrollStalePanel);
        await Case("Неопределённый wheel включает emergency, reconcile и не повторяется", IndeterminateScroll);
        await Case("Wheel в другой список включает emergency без клика и retry", MisroutedScroll);
        await Case("Одновременный сдвиг целевого и чужого scroll также блокируется", BothScrollsMoved);
        await Case("Клик запрещён без точного causal pointer/hover proof", MissingPointerProof);
        await Case("Невидимый scroll 0/50 не блокирует exact hovered input-кнопку", HiddenScrollPhantomTarget);
        await Case("Нормальный рецепт использует не более двух полных snapshot на клик", FastPathFullReadBudget);
        await Case("External decision отвергает stale epoch", ExternalDecisionStaleEpoch);
        await Case("Abort текущего epoch отменяет двухснимочную external validation", ExternalAbortDuringValidation);
        await Case("External accept отвергает непричинный повторный snapshot", ExternalAcceptanceRepeatedSequenceRejected);
        await Case("Фиксированная fixture-калибровка не изменена", () =>
        {
            Assert(fixtureHash.SequenceEqual(SHA256.HashData(File.ReadAllBytes(fixture))), "Fixture была изменена");
            return Task.CompletedTask;
        });

        string reportDirectory = Path.Combine(root, ".test-results");
        Directory.CreateDirectory(reportDirectory);
        int failed = Results.Count(result => !result.Passed);
        var report = new
        {
            schemaVersion = 1,
            offlineOnly = true,
            total = Results.Count,
            failed,
            fixtureSha256 = Convert.ToHexString(fixtureHash),
            cases = Results,
        };
        File.WriteAllText(Path.Combine(reportDirectory, "chemistry-executor-report.json"),
            JsonSerializer.Serialize(report, ChemistryVirtual.Json));
        Console.WriteLine($"ChemMaster executor: {Results.Count - failed}/{Results.Count} passed, {failed} failed. No SS14/Windows input.");
        foreach (var result in Results.Where(result => !result.Passed))
            Console.WriteLine("FAIL " + result.Name + ": " + result.Error);
        return failed == 0 ? 0 : 1;
    }

    private static async Task Case(string name, Func<Task> test)
    {
        try
        {
            await test().ConfigureAwait(false);
            Results.Add(new Result(name, true, null));
        }
        catch (Exception ex)
        {
            Results.Add(new Result(name, false, ex.Message));
        }
    }

    private static List<VirtualReagent> Stock(params (string Id, decimal Amount)[] rows) =>
        rows.Select(row => new VirtualReagent(row.Id, row.Amount)).ToList();

    private static async Task FullRecipe()
    {
        using var harness = await Harness.Create(Stock(("Inaprovaline", 5), ("Carbon", 5)));
        await harness.Executor.StartAsync("Bicaridine=10", ChemistryTargetMode.Ensure);
        Equal(harness.Executor.Progress.State, ChemMasterExecutorState.Completed, harness.Executor.Progress.Message);
        Equal(harness.World.ClickCount, 3, "Число физических кликов");
        Equal(harness.World.AppliedClickCount, 3, "Число изменений модели");
        Equal(harness.World.Machine.Buffer.Get("Bicaridine"), 1000, "Итоговый бикаридин");
        Equal(harness.World.Machine.Beaker.Volume, 0, "Мензурка возвращена пустой");
        var events = ReadJournal(harness.Executor.JournalPath);
        Equal(events.Count(row => row == "click-before"), 3, "Транзакции до клика");
        Equal(events.Count(row => row == "click-confirmed"), 3, "Подтверждения клика");
    }

    private static async Task DelayedState()
    {
        using var harness = await Harness.Create(Stock(("Inaprovaline", 5), ("Carbon", 5)));
        harness.World.StaleReadsAfterClick = 3;
        await harness.Executor.StartAsync("Bicaridine=10", ChemistryTargetMode.Ensure);
        Equal(harness.Executor.Progress.State, ChemMasterExecutorState.Completed, harness.Executor.Progress.Message);
        Equal(harness.World.ClickCount, 3, "Задержка не должна повторять ввод");
        Assert(harness.World.ReadCount >= 13, "Не было реального ожидания нескольких stale snapshot");
    }

    private static async Task NoStateChange()
    {
        using var harness = await Harness.Create(Stock(("Inaprovaline", 5), ("Carbon", 5)));
        harness.World.SuppressClicks = true;
        await harness.Executor.StartAsync("Bicaridine=10", ChemistryTargetMode.Ensure);
        Equal(harness.Executor.Progress.State, ChemMasterExecutorState.Failed, "Timeout должен завершиться Failed");
        Equal(harness.World.ClickCount, 1, "Клик после timeout повторился");
        Equal(harness.World.AppliedClickCount, 0, "Fake не должен менять State");
        Assert(harness.Executor.Progress.Message.Contains("не появился", StringComparison.OrdinalIgnoreCase), "Нет понятной причины timeout");
    }

    private static async Task ExternalChangeAbort()
    {
        using var harness = await Harness.Create(Stock(("Inaprovaline", 5), ("Carbon", 5)));
        harness.World.OnRead(2, world => world.Machine.Buffer.Add("Iron", 100));
        var run = harness.Executor.StartAsync("Bicaridine=10", ChemistryTargetMode.Ensure);
        await WaitUntil(() => harness.Executor.IsExternalPause, "Не возникла внешняя пауза");
        Equal(harness.World.ClickCount, 0, "Ввод выполнен после внешнего изменения");
        harness.Executor.AbortExternalState();
        await run;
        Equal(harness.Executor.Progress.State, ChemMasterExecutorState.Aborted, "Явный abort");
    }

    private static async Task PartialTransfer()
    {
        using var harness = await Harness.Create(Stock(("Inaprovaline", 5), ("Carbon", 5)));
        harness.World.PartialNextClick = true;
        var run = harness.Executor.StartAsync("Bicaridine=10", ChemistryTargetMode.Ensure);
        await WaitUntil(() => harness.Executor.IsExternalPause, "Частичный перенос не вызвал паузу");
        Equal(harness.World.ClickCount, 1, "Неверное число кликов до mismatch");
        Assert(harness.World.Machine.Beaker.Volume > 0, "Контрпример должен оставить непустую мензурку");
        harness.Executor.AcceptExternalStateAndReplan();
        await WaitUntil(() => harness.Executor.IsExternalPause &&
            harness.Executor.Progress.Message.Contains("Мензурка не пуста", StringComparison.OrdinalIgnoreCase),
            "Replan ошибочно разрешён с непустой мензуркой");
        Equal(harness.World.ClickCount, 1, "Во время небезопасной паузы появился новый клик");
        harness.Executor.AbortExternalState();
        await run;
        Equal(harness.Executor.Progress.State, ChemMasterExecutorState.Aborted, "Abort после mismatch");
    }

    private static async Task MovingScroll()
    {
        var stock = Rules.Reagents.Keys
            .Where(id => id != "Inaprovaline" && id != "Carbon" && id != "Bicaridine")
            .Take(14)
            .Select(id => new VirtualReagent(id, 1))
            .ToList();
        stock.Add(new VirtualReagent("Inaprovaline", 5));
        stock.Add(new VirtualReagent("Carbon", 5));
        using var harness = await Harness.Create(stock);
        await harness.Executor.StartAsync("Bicaridine=10", ChemistryTargetMode.Ensure);
        Equal(harness.Executor.Progress.State, ChemMasterExecutorState.Completed, harness.Executor.Progress.Message);
        Assert(harness.World.ScrollCount >= 1, "Скрытая строка не прокручивалась");
        Assert(harness.World.ScrollTargetChangeCount >= 1, "ValueTarget не менялся между snapshot");
        Assert(harness.World.FastReadCount >= harness.World.ScrollCount,
            "Анимация прокрутки не использовала быстрые причинные snapshot");
        Assert(harness.World.FullReadCount <= harness.World.ScrollCount + harness.World.ClickCount * 2 + 4,
            $"Слишком много полных snapshot: full={harness.World.FullReadCount}, wheel={harness.World.ScrollCount}, click={harness.World.ClickCount}");
        var journalLines = ReadLinesShared(harness.Executor.JournalPath);
        using var hintDocument = JsonDocument.Parse(journalLines.First(line =>
            line.Contains("\"eventName\":\"scroll-stable-hint\"", StringComparison.Ordinal)));
        using var scrollConfirmedDocument = JsonDocument.Parse(journalLines.First(line =>
            line.Contains("\"eventName\":\"scroll-confirmed\"", StringComparison.Ordinal)));
        var hintRead = hintDocument.RootElement.GetProperty("payload").GetProperty("read");
        var confirmedRead = scrollConfirmedDocument.RootElement.GetProperty("payload").GetProperty("read");
        AssertReadTelemetry(hintRead, expectedComplete: false, expectedPath: "fast-cache-hit");
        AssertReadTelemetry(confirmedRead, expectedComplete: true, expectedPath: "full");
        Assert(confirmedRead.GetProperty("sequence").GetInt64() > hintRead.GetProperty("sequence").GetInt64() &&
            confirmedRead.GetProperty("observedAt").GetDateTimeOffset() >
            hintRead.GetProperty("observedAt").GetDateTimeOffset(),
            "Full scroll control не является причинным продолжением fast hint");
        Equal(harness.World.ClickCount, 3, "Прокрутка не должна добавлять клики по реагентам");
    }

    private static async Task FullAmbiguityAfterFastHint()
    {
        using var harness = await Harness.Create(HiddenBicaridineStock());
        harness.World.InvalidateFullControlAfterFast = true;
        await harness.Executor.StartAsync("Bicaridine=10", ChemistryTargetMode.Ensure);
        Equal(harness.Executor.Progress.State, ChemMasterExecutorState.Failed,
            "Неоднозначный полный snapshot должен завершить run");
        Equal(harness.World.ScrollCount, 1, "После неоднозначного полного snapshot wheel повторился");
        Equal(harness.World.ClickCount, 0, "Cached hint разрешил click без полного контроля");
        Assert(harness.Executor.LastSnapshot?.Observation.CandidateSetComplete == true,
            "Не был выполнен полный candidate scan после fast hint");
        Assert(!ReadJournal(harness.Executor.JournalPath).Contains("scroll-confirmed"),
            "Неоднозначный полный snapshot ошибочно записан как scroll-confirmed");
    }

    private static async Task InvalidFastHintForcesFullScan()
    {
        using var harness = await Harness.Create(HiddenBicaridineStock());
        harness.World.InvalidateFastReadsUntilFullControl = true;
        await harness.Executor.StartAsync("Bicaridine=10", ChemistryTargetMode.Ensure);
        Equal(harness.Executor.Progress.State, ChemMasterExecutorState.Completed,
            "Некорректный cached hint не восстановился через полный scan");
        Assert(!harness.World.InvalidateFastReadsUntilFullControl,
            "Executor продолжил cached polling без полного scan");
        Assert(harness.World.FastReadCount >= 1 && harness.World.FullReadCount >= 1,
            "Не выполнена цепочка cached hint -> full scan");
        Equal(harness.World.ClickCount, 3, "Fallback после cached hint изменил число reagent clicks");
    }

    private static async Task FastFallbackHintStillRequiresFinalFull()
    {
        using var harness = await Harness.Create(HiddenBicaridineStock());
        harness.World.FastReadsFallbackToComplete = true;
        await harness.Executor.StartAsync("Bicaridine=10", ChemistryTargetMode.Ensure);
        Equal(harness.Executor.Progress.State, ChemMasterExecutorState.Completed,
            "Fast fallback-full нарушил нормальный scroll path");
        var journalLines = ReadLinesShared(harness.Executor.JournalPath);
        using var hintDocument = JsonDocument.Parse(journalLines.First(line =>
            line.Contains("\"eventName\":\"scroll-stable-hint\"", StringComparison.Ordinal)));
        using var confirmedDocument = JsonDocument.Parse(journalLines.First(line =>
            line.Contains("\"eventName\":\"scroll-confirmed\"", StringComparison.Ordinal)));
        var hintRead = hintDocument.RootElement.GetProperty("payload").GetProperty("read");
        var confirmedRead = confirmedDocument.RootElement.GetProperty("payload").GetProperty("read");
        AssertReadTelemetry(hintRead, expectedComplete: true, expectedPath: "fast-fallback-full");
        AssertReadTelemetry(confirmedRead, expectedComplete: true, expectedPath: "full");
        Assert(confirmedRead.GetProperty("sequence").GetInt64() > hintRead.GetProperty("sequence").GetInt64(),
            "Fallback-full hint был ошибочно использован вместо отдельного финального full");
        Equal(harness.World.ClickCount, 3, "Fallback-full hint изменил число reagent clicks");
    }

    private static async Task CausalTimeRegressionAfterFastHint()
    {
        using var harness = await Harness.Create(HiddenBicaridineStock());
        harness.World.RegressTimestampOnNextFullAfterFast = true;
        await harness.Executor.StartAsync("Bicaridine=10", ChemistryTargetMode.Ensure);
        Equal(harness.Executor.Progress.State, ChemMasterExecutorState.Failed,
            "Регресс ObservedAt должен завершить run");
        Equal(harness.World.ScrollCount, 1, "После регресса времени wheel повторился");
        Equal(harness.World.ClickCount, 0, "Snapshot с непричинным временем разрешил click");
        Assert(!ReadJournal(harness.Executor.JournalPath).Contains("scroll-confirmed"),
            "Непричинный snapshot ошибочно записан как scroll-confirmed");
    }

    private static async Task LateStableTargetDrift()
    {
        var stock = Rules.Reagents.Keys
            .Where(id => id != "Inaprovaline" && id != "Carbon" && id != "Bicaridine")
            .Take(14)
            .Select(id => new VirtualReagent(id, 1))
            .ToList();
        stock.Add(new VirtualReagent("Inaprovaline", 5));
        stock.Add(new VirtualReagent("Carbon", 5));
        using var harness = await Harness.Create(stock);
        harness.World.DriftTargetAfterFirstStable = true;
        await harness.Executor.StartAsync("Bicaridine=10", ChemistryTargetMode.Ensure);
        Equal(harness.Executor.Progress.State, ChemMasterExecutorState.Failed,
            "Поздний ValueTarget drift должен завершить run ошибкой");
        Assert(harness.World.EmergencyStopped, "Поздний ValueTarget drift не включил emergency");
        Equal(harness.World.ScrollCount, 1, "Wheel был повторён после позднего drift");
        Equal(harness.World.ClickCount, 0, "Реагент был нажат после позднего drift");
    }

    private static async Task PanelClosed()
    {
        using var harness = await Harness.Create(Stock(("Inaprovaline", 5), ("Carbon", 5)));
        harness.World.OnRead(2, world => world.InterfaceOpen = false);
        await harness.Executor.StartAsync("Bicaridine=10", ChemistryTargetMode.Ensure);
        Equal(harness.Executor.Progress.State, ChemMasterExecutorState.Failed, "Закрытая панель");
        Equal(harness.World.ClickCount, 0, "Клик при закрытой панели");
    }

    private static async Task InactiveWindow()
    {
        using var harness = await Harness.Create(Stock(("Inaprovaline", 5), ("Carbon", 5)));
        harness.World.OnRead(2, world => world.WindowActive = false);
        var run = harness.Executor.StartAsync("Bicaridine=10", ChemistryTargetMode.Ensure);
        await WaitUntil(() => harness.Executor.Progress.State == ChemMasterExecutorState.Paused, "Нет паузы при неактивном окне");
        Equal(harness.World.ClickCount, 0, "Клик в неактивное окно");
        harness.Executor.Resume();
        harness.Executor.Resume();
        await run;
        Equal(harness.Executor.Progress.State, ChemMasterExecutorState.Completed, harness.Executor.Progress.Message);
        Equal(harness.World.ActivationCallCount, 1, "Resume должен активировать SS14 ровно один раз");
    }

    private static async Task ResumeActivationFailure()
    {
        using var harness = await Harness.Create(Stock(("Inaprovaline", 5), ("Carbon", 5)));
        harness.World.OnRead(2, world => world.WindowActive = false);
        harness.World.ActivationSucceeds = false;
        var run = harness.Executor.StartAsync("Bicaridine=10", ChemistryTargetMode.Ensure);
        await WaitUntil(() => harness.Executor.Progress.State == ChemMasterExecutorState.Paused,
            "Нет паузы перед неудачной активацией");
        Throws<InvalidOperationException>(() => { harness.Executor.Resume(); return null; });
        Equal(harness.Executor.Progress.State, ChemMasterExecutorState.Paused,
            "Неудачная активация сняла паузу");
        Equal(harness.World.ClickCount, 0, "Ввод после неудачной активации");
        harness.Executor.Cancel();
        await run;
    }

    private static async Task ResumeActivationDoesNotStick()
    {
        using var harness = await Harness.Create(Stock(("Inaprovaline", 5), ("Carbon", 5)));
        harness.World.OnRead(2, world => world.WindowActive = false);
        harness.World.ActivationSetsWindowActive = false;
        var run = harness.Executor.StartAsync("Bicaridine=10", ChemistryTargetMode.Ensure);
        await WaitUntil(() => harness.Executor.Progress.State == ChemMasterExecutorState.Paused,
            "Нет исходной паузы");
        harness.Executor.Resume();
        await WaitUntil(() => harness.World.ReadCount >= 3 &&
            harness.Executor.Progress.State == ChemMasterExecutorState.Paused,
            "После потери активированного фокуса пауза не вернулась");
        Equal(harness.World.ClickCount, 0, "Ввод при неустойчивом фокусе");
        Equal(harness.World.ActivationCallCount, 1, "Лишняя попытка активации");
        harness.Executor.Cancel();
        await run;
    }

    private static async Task ResumeJournalFailure()
    {
        using var harness = await Harness.Create(Stock(("Inaprovaline", 5), ("Carbon", 5)), journalFactory:
            (root, directory) => new FaultingJournal(new ActionJournal(root, directory), "resume-requested"));
        harness.World.OnRead(2, world => world.WindowActive = false);
        var run = harness.Executor.StartAsync("Bicaridine=10", ChemistryTargetMode.Ensure);
        await WaitUntil(() => harness.Executor.Progress.State == ChemMasterExecutorState.Paused,
            "Нет исходной паузы перед fault журнала");
        Throws<IOException>(() => { harness.Executor.Resume(); return null; });
        Equal(harness.Executor.Progress.State, ChemMasterExecutorState.Paused,
            "Ошибка durable resume-записи сняла паузу");
        Equal(harness.World.ClickCount, 0, "Ввод прошёл после ошибки durable resume-записи");
        Equal(harness.World.ActivationCallCount, 1, "Resume не выполнил ровно одну попытку активации");
        harness.Executor.Cancel();
        await run;
    }

    private static async Task WindowResize()
    {
        using var harness = await Harness.Create(Stock(("Inaprovaline", 5), ("Carbon", 5)));
        harness.World.ClientWidth += 1;
        await harness.Executor.StartAsync("Bicaridine=10", ChemistryTargetMode.Ensure);
        Equal(harness.Executor.Progress.State, ChemMasterExecutorState.Failed, "Resize должен блокировать");
        Equal(harness.World.ClickCount, 0, "Клик после resize");
    }

    private static async Task ShiftedWindow()
    {
        using var harness = await Harness.Create(Stock(("Inaprovaline", 5), ("Carbon", 5)));
        harness.World.WindowLeft += 240;
        harness.World.WindowTop += 120;
        harness.World.ClientScreenX += 240;
        harness.World.ClientScreenY += 120;
        int shiftedLeft = harness.World.WindowLeft;
        await harness.Executor.StartAsync("Bicaridine=10", ChemistryTargetMode.Ensure);
        Equal(harness.Executor.Progress.State, ChemMasterExecutorState.Completed, harness.Executor.Progress.Message);
        Assert(harness.World.Clicks.All(click => click.WindowLeft == shiftedLeft), "Использовано старое положение окна");
        Assert(harness.World.Clicks.All(click => click.ClientX >= 0 && click.ClientX < harness.World.ClientWidth), "Координата не client-relative");
    }

    private static async Task WrongDpi()
    {
        using var harness = await Harness.Create(Stock(("Inaprovaline", 5), ("Carbon", 5)));
        harness.World.Dpi = 120;
        await harness.Executor.StartAsync("Bicaridine=10", ChemistryTargetMode.Ensure);
        Equal(harness.Executor.Progress.State, ChemMasterExecutorState.Failed, "DPI должен блокировать");
        Equal(harness.World.ClickCount, 0, "Клик с неправильным DPI");
    }

    private static async Task NonEmptyBeaker()
    {
        using var harness = await Harness.Create(Stock(("Inaprovaline", 5), ("Carbon", 5)));
        harness.World.Machine.Beaker.Add("Water", 100);
        await harness.Executor.StartAsync("Bicaridine=10", ChemistryTargetMode.Ensure);
        Equal(harness.Executor.Progress.State, ChemMasterExecutorState.Failed, "Грязная мензурка должна блокировать");
        Equal(harness.World.ClickCount, 0, "Клик с грязной мензуркой");
    }

    private static async Task PauseResume()
    {
        using var harness = await Harness.Create(Stock(("Inaprovaline", 5), ("Carbon", 5)));
        harness.World.ReadDelayMilliseconds = 50;
        var run = harness.Executor.StartAsync("Bicaridine=10", ChemistryTargetMode.Ensure);
        harness.Executor.Pause();
        await WaitUntil(() => harness.Executor.Progress.State == ChemMasterExecutorState.Paused, "Пауза не подтверждена");
        Equal(harness.World.ClickCount, 0, "Клик во время паузы");
        harness.Executor.Resume();
        await run;
        Equal(harness.Executor.Progress.State, ChemMasterExecutorState.Completed, harness.Executor.Progress.Message);
        Equal(harness.World.ClickCount, 3, "Шаг потерян или повторён после resume");
    }

    private static async Task PauseJournalFailure()
    {
        using var harness = await Harness.Create(Stock(("Inaprovaline", 5), ("Carbon", 5)), journalFactory:
            (root, directory) => new FaultingJournal(new ActionJournal(root, directory), "pause-requested"));
        harness.World.ReadDelayMilliseconds = 50;
        var run = harness.Executor.StartAsync("Bicaridine=10", ChemistryTargetMode.Ensure);
        Throws<IOException>(() => { harness.Executor.Pause(); return null; });
        await WaitUntil(() => harness.Executor.Progress.State == ChemMasterExecutorState.Paused,
            "Ошибка durable Pause сняла latch");
        Equal(harness.World.ClickCount, 0, "После ошибки durable Pause появился ввод");
        harness.Executor.Cancel();
        await run;
    }

    private static async Task Cancel()
    {
        using var harness = await Harness.Create(Stock(("Inaprovaline", 5), ("Carbon", 5)));
        harness.World.ReadDelayMilliseconds = 50;
        var run = harness.Executor.StartAsync("Bicaridine=10", ChemistryTargetMode.Ensure);
        harness.Executor.Pause();
        await WaitUntil(() => harness.Executor.Progress.State == ChemMasterExecutorState.Paused, "Нет паузы перед cancel");
        harness.Executor.Cancel();
        await run;
        Equal(harness.Executor.Progress.State, ChemMasterExecutorState.Aborted, "Cancel");
        Equal(harness.World.ClickCount, 0, "Клик после cancel");
    }

    private static async Task EmergencyStopReset()
    {
        using var harness = await Harness.Create(Stock(("Inaprovaline", 5), ("Carbon", 5)));
        harness.World.ReadDelayMilliseconds = 50;
        var run = harness.Executor.StartAsync("Bicaridine=10", ChemistryTargetMode.Ensure);
        harness.Executor.Pause();
        await WaitUntil(() => harness.Executor.Progress.State == ChemMasterExecutorState.Paused, "Нет паузы перед emergency stop");
        harness.Executor.EmergencyStop();
        await run;
        Equal(harness.Executor.Progress.State, ChemMasterExecutorState.Aborted, "Emergency stop");
        Assert(harness.World.EmergencyStopped, "Input driver не заблокирован");
        Throws<InvalidOperationException>(() => harness.Executor.StartAsync("Bicaridine=10", ChemistryTargetMode.Ensure));
        harness.Executor.ResetEmergencyStop();
        Equal(harness.Executor.Progress.State, ChemMasterExecutorState.Idle, "Reset должен требовать новый запуск");
        Assert(!harness.World.EmergencyStopped, "Блокировка не снята явно");
        harness.World.ReadDelayMilliseconds = 0;
        await harness.Executor.StartAsync("Bicaridine=10", ChemistryTargetMode.Ensure);
        Equal(harness.Executor.Progress.State, ChemMasterExecutorState.Completed, harness.Executor.Progress.Message);
    }

    private static async Task SafeExternalReplan()
    {
        using var harness = await Harness.Create(Stock(("Inaprovaline", 5), ("Carbon", 5)));
        harness.World.OnRead(2, world => world.Machine.Buffer.Add("Iron", 100));
        var run = harness.Executor.StartAsync("Bicaridine=10", ChemistryTargetMode.Ensure);
        await WaitUntil(() => harness.Executor.IsExternalPause, "Нет паузы для безопасного replan");
        Equal(harness.World.Machine.Beaker.Volume, 0, "Replan разрешается только на чистой границе");
        harness.Executor.AcceptExternalStateAndReplan();
        await run;
        Equal(harness.Executor.Progress.State, ChemMasterExecutorState.Completed, harness.Executor.Progress.Message);
        Equal(harness.World.Machine.Buffer.Get("Iron"), 100, "Внешний запас потерян при replan");
        Equal(harness.World.Machine.Buffer.Get("Bicaridine"), 1000, "Цель после replan");
    }

    private static async Task JsonlJournal()
    {
        using var harness = await Harness.Create(Stock(("Inaprovaline", 5), ("Carbon", 5)));
        await harness.Executor.StartAsync("Bicaridine=10", ChemistryTargetMode.Ensure);
        string[] lines = ReadLinesShared(harness.Executor.JournalPath);
        Assert(lines.Length > 5, "Журнал пуст");
        var eventNames = new List<string>();
        foreach (string line in lines)
        {
            using var document = JsonDocument.Parse(line);
            var row = document.RootElement;
            Equal(row.GetProperty("schemaVersion").GetInt32(), 1, "schemaVersion журнала");
            Assert(row.TryGetProperty("time", out _), "Нет времени");
            Assert(row.TryGetProperty("state", out _), "Нет состояния автомата");
            Assert(row.TryGetProperty("payload", out _), "Нет payload");
            eventNames.Add(row.GetProperty("eventName").GetString()!);
        }
        Assert(eventNames.Contains("click-before") && eventNames.Contains("click-confirmed") && eventNames.Contains("completed"),
            "Нет транзакционных событий журнала");
        using var clickDocument = JsonDocument.Parse(lines.First(line => line.Contains("\"eventName\":\"click-before\"", StringComparison.Ordinal)));
        var payload = clickDocument.RootElement.GetProperty("payload");
        Assert(payload.TryGetProperty("action", out _) && payload.TryGetProperty("row", out _) &&
            payload.TryGetProperty("point", out _) && payload.TryGetProperty("snapshot", out _), "click-before неполон");
        var clickBeforeRead = payload.GetProperty("read");
        AssertReadTelemetry(clickBeforeRead, expectedComplete: true, expectedPath: "full");
        using var confirmedDocument = JsonDocument.Parse(lines.First(line => line.Contains("\"eventName\":\"click-confirmed\"", StringComparison.Ordinal)));
        var confirmedPayload = confirmedDocument.RootElement.GetProperty("payload");
        Assert(confirmedPayload.TryGetProperty("expectedReactions", out _) &&
            confirmedPayload.TryGetProperty("actualReactions", out _) &&
            confirmedPayload.TryGetProperty("reactionsDeterministic", out _) &&
            confirmedPayload.TryGetProperty("reactionsMatch", out _), "click-confirmed не содержит reconcile реакций");
        var clickConfirmedRead = confirmedPayload.GetProperty("read");
        AssertReadTelemetry(clickConfirmedRead, expectedComplete: true, expectedPath: "full");
        Assert(clickConfirmedRead.GetProperty("sequence").GetInt64() >
            clickBeforeRead.GetProperty("sequence").GetInt64(),
            "click-confirmed telemetry не имеет нового sequence");
    }

    private static Task ImmediateJournalVisibility()
    {
        string temporaryRoot = Path.Combine(Path.GetTempPath(),
            "ss14-chemmaster-journal-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryRoot);
        try
        {
            using var journal = new ActionJournal(temporaryRoot, "logs");
            journal.Write("first-visible", ChemMasterExecutorState.Idle, new { sequence = 1 });

            var file = new FileInfo(journal.Path);
            file.Refresh();
            Assert(file.Length > 0, "Первая запись не увеличила файл до Dispose");
            string[] firstRead = ReadLinesShared(journal.Path);
            Equal(firstRead.Length, 1, "Первая запись не видна отдельному читателю до Dispose");
            using (var first = JsonDocument.Parse(firstRead[0]))
            {
                Equal(first.RootElement.GetProperty("eventName").GetString()!, "first-visible",
                    "Первая видимая запись повреждена");
                Equal(first.RootElement.GetProperty("payload").GetProperty("sequence").GetInt32(), 1,
                    "Payload первой видимой записи");
            }

            journal.Write("second-visible", ChemMasterExecutorState.Executing, new { sequence = 2 });
            string[] secondRead = ReadLinesShared(journal.Path);
            Equal(secondRead.Length, 2, "Вторая запись не видна отдельному читателю до Dispose");
            using var second = JsonDocument.Parse(secondRead[1]);
            Equal(second.RootElement.GetProperty("eventName").GetString()!, "second-visible",
                "Вторая видимая запись повреждена");
            Equal(second.RootElement.GetProperty("payload").GetProperty("sequence").GetInt32(), 2,
                "Payload второй видимой записи");
        }
        finally
        {
            try { Directory.Delete(temporaryRoot, true); } catch { }
        }
        return Task.CompletedTask;
    }

    private static async Task VisibleMovingScroll()
    {
        using var harness = await Harness.Create(Stock(("Inaprovaline", 5), ("Carbon", 5),
            ("Iron", 1), ("Copper", 1), ("Water", 1), ("Sugar", 1)));
        harness.World.OnRead(2, world => world.QueueVisibleMovingBufferScroll());
        await harness.Executor.StartAsync("Bicaridine=10", ChemistryTargetMode.Ensure);
        Equal(harness.Executor.Progress.State, ChemMasterExecutorState.Completed, harness.Executor.Progress.Message);
        Equal(harness.World.ScrollCount, 0, "Видимая строка не должна требовать wheel");
        Assert(harness.World.FullReadCount <= harness.World.ClickCount * 2 + 2,
            "Видимая строка получила лишние полные snapshot");
    }

    private static async Task RepeatedSequenceRejected()
    {
        using var harness = await Harness.Create(Stock(("Inaprovaline", 5), ("Carbon", 5)));
        harness.World.OnRead(2, world => world.RepeatNextSequence = true);
        await harness.Executor.StartAsync("Bicaridine=10", ChemistryTargetMode.Ensure);
        Equal(harness.Executor.Progress.State, ChemMasterExecutorState.Failed,
            "Повторный sequence должен блокировать run");
        Equal(harness.World.ClickCount, 0, "Клик по непричинному snapshot");
    }

    private static async Task FastPathFullReadBudget()
    {
        using var harness = await Harness.Create(Stock(("Inaprovaline", 5), ("Carbon", 5)));
        await harness.Executor.StartAsync("Bicaridine=10", ChemistryTargetMode.Ensure);
        Equal(harness.Executor.Progress.State, ChemMasterExecutorState.Completed, harness.Executor.Progress.Message);
        Equal(harness.World.ClickCount, 3, "Контрольный рецепт");
        Assert(harness.World.FullReadCount <= harness.World.ClickCount * 2 + 1,
            $"Fast path сделал {harness.World.FullReadCount} полных reads на {harness.World.ClickCount} клика");
    }

    private static async Task PauseResumeExternalMutation()
    {
        using var harness = await Harness.Create(Stock(("Inaprovaline", 5), ("Carbon", 5)));
        harness.World.ReadDelayMilliseconds = 50;
        var run = harness.Executor.StartAsync("Bicaridine=10", ChemistryTargetMode.Ensure);
        harness.Executor.Pause();
        await WaitUntil(() => harness.Executor.Progress.State == ChemMasterExecutorState.Paused,
            "Обычная пауза не подтверждена");
        harness.World.Machine.Buffer.Add("Iron", 100);
        harness.Executor.Resume();
        await WaitUntil(() => harness.Executor.IsExternalPause, "Resume не перечитал изменённый State");
        Equal(harness.World.ClickCount, 0, "Клик прошёл по state до pause");
        harness.Executor.AcceptExternalStateAndReplan();
        await run;
        Equal(harness.Executor.Progress.State, ChemMasterExecutorState.Completed, harness.Executor.Progress.Message);
        Equal(harness.World.Machine.Buffer.Get("Iron"), 100, "Внешнее изменение потеряно после replan");
    }

    private static async Task ExternalDecisionNoLostWakeup()
    {
        using var harness = await Harness.Create(Stock(("Inaprovaline", 5), ("Carbon", 5)));
        harness.World.OnRead(2, world => world.Machine.Buffer.Add("Iron", 100));
        var accepted = 0;
        harness.Executor.ProgressChanged += progress =>
        {
            if (progress.State == ChemMasterExecutorState.Paused && harness.Executor.IsExternalPause &&
                Interlocked.Exchange(ref accepted, 1) == 0)
                harness.Executor.AcceptExternalStateAndReplan();
        };
        await harness.Executor.StartAsync("Bicaridine=10", ChemistryTargetMode.Ensure);
        Equal(harness.Executor.Progress.State, ChemMasterExecutorState.Completed, harness.Executor.Progress.Message);
        Equal(accepted, 1, "Callback не принял external decision");
        Assert(harness.Executor.ExternalDecisionEpoch >= 1, "Epoch решения не увеличился");
    }

    private static async Task PauseCommitRace()
    {
        using var harness = await Harness.Create(Stock(("Inaprovaline", 5), ("Carbon", 5)));
        harness.World.BlockNextClick = true;
        var run = harness.Executor.StartAsync("Bicaridine=10", ChemistryTargetMode.Ensure);
        await WaitUntil(() => harness.World.ClickEntered.IsSet, "Fake click не вошёл в commit");
        var pause = Task.Run(() => harness.Executor.Pause());
        await Task.Delay(75);
        Assert(!pause.IsCompleted, "Pause обошёл input commit gate");
        harness.World.ReleaseClick.Set();
        await pause;
        await WaitUntil(() => harness.Executor.Progress.State == ChemMasterExecutorState.Paused,
            "Pause после завершения commit не вступил в силу");
        Equal(harness.World.ClickCount, 1, "Commit был повторён до Resume");
        harness.Executor.Resume();
        await run;
        Equal(harness.Executor.Progress.State, ChemMasterExecutorState.Completed, harness.Executor.Progress.Message);
    }

    private static async Task ClickPreflightFocusLoss()
    {
        CallbackJournal? callbackJournal = null;
        using var harness = await Harness.Create(Stock(("Inaprovaline", 5), ("Carbon", 5)),
            journalFactory: (root, directory) =>
                callbackJournal = new CallbackJournal(new ActionJournal(root, directory)));
        var fired = 0;
        callbackJournal!.OnWrite = eventName =>
        {
            if (eventName == "click-before" && Interlocked.Exchange(ref fired, 1) == 0)
                harness.World.LoseWindowFocus();
        };
        await harness.Executor.StartAsync("Bicaridine=10", ChemistryTargetMode.Ensure);
        Equal(harness.Executor.Progress.State, ChemMasterExecutorState.Failed,
            "Потеря фокуса непосредственно перед SendInput должна блокировать run");
        Equal(harness.World.ClickCount, 0, "Клик прошёл после потери фокуса");
    }

    private static async Task ScrollPreflightPointerDrift()
    {
        CallbackJournal? callbackJournal = null;
        using var harness = await Harness.Create(HiddenBicaridineStock(),
            journalFactory: (root, directory) =>
                callbackJournal = new CallbackJournal(new ActionJournal(root, directory)));
        var fired = 0;
        callbackJournal!.OnWrite = eventName =>
        {
            if (eventName == "scroll-before" && Interlocked.Exchange(ref fired, 1) == 0)
                harness.World.MovePhysicalPointerAway();
        };
        await harness.Executor.StartAsync("Bicaridine=10", ChemistryTargetMode.Ensure);
        Equal(harness.Executor.Progress.State, ChemMasterExecutorState.Failed,
            "Сдвиг курсора непосредственно перед wheel должен блокировать run");
        Equal(harness.World.ScrollCount, 0, "Wheel прошёл после сдвига курсора");
        Equal(harness.World.ClickCount, 0, "После отклонённого wheel появился reagent click");
    }

    private static async Task CancelCommitRace()
    {
        using var harness = await Harness.Create(Stock(("Inaprovaline", 5), ("Carbon", 5)));
        harness.World.BlockNextClick = true;
        var run = harness.Executor.StartAsync("Bicaridine=10", ChemistryTargetMode.Ensure);
        await WaitUntil(() => harness.World.ClickEntered.IsSet, "Fake click не вошёл в commit");
        var cancel = Task.Run(() => harness.Executor.Cancel());
        await Task.Delay(75);
        Assert(!cancel.IsCompleted, "Cancel обошёл input commit gate");
        harness.World.ReleaseClick.Set();
        await cancel;
        await run;
        Equal(harness.Executor.Progress.State, ChemMasterExecutorState.Aborted, harness.Executor.Progress.Message);
        Equal(harness.World.ClickCount, 1, "Cancel допустил повтор commit");
        Assert(harness.Executor.LastSnapshot != null &&
            SnapshotInventory.Sum(SnapshotInventory.From(harness.Executor.LastSnapshot).Beaker) == harness.World.Machine.Beaker.Volume,
            "Post-click State не reconciled перед Cancel completion");
    }

    private static async Task JournalFaultAfterClick()
    {
        using var harness = await Harness.Create(Stock(("Inaprovaline", 5), ("Carbon", 5)), journalFactory:
            (root, directory) => new FaultingJournal(new ActionJournal(root, directory), "click-confirmed"));
        await harness.Executor.StartAsync("Bicaridine=10", ChemistryTargetMode.Ensure);
        Equal(harness.Executor.Progress.State, ChemMasterExecutorState.Failed, "Fault журнала должен быть terminal после reconcile");
        Equal(harness.World.ClickCount, 1, "Journal fault повторил физический клик");
        Equal(harness.World.AppliedClickCount, 1, "Первый клик должен физически примениться");
        Assert(harness.Executor.LastSnapshot != null &&
            SnapshotInventory.Sum(SnapshotInventory.From(harness.Executor.LastSnapshot).Beaker) == harness.World.Machine.Beaker.Volume,
            "Journal fault оборвал reconcile");
    }

    private static async Task JournalFaultAfterClickSent()
    {
        using var harness = await Harness.Create(Stock(("Inaprovaline", 5), ("Carbon", 5)), journalFactory:
            (root, directory) => new FaultingJournal(new ActionJournal(root, directory), "click-sent"));
        await harness.Executor.StartAsync("Bicaridine=10", ChemistryTargetMode.Ensure);
        Equal(harness.Executor.Progress.State, ChemMasterExecutorState.Failed,
            "Fault durable click-sent был потерян после reconcile");
        Equal(harness.World.ClickCount, 1, "Fault click-sent повторил физический клик");
        Equal(harness.World.AppliedClickCount, 1, "Первый клик должен физически примениться");
        Assert(harness.Executor.LastSnapshot != null &&
            SnapshotInventory.Sum(SnapshotInventory.From(harness.Executor.LastSnapshot).Beaker) ==
            harness.World.Machine.Beaker.Volume,
            "Fault click-sent оборвал обязательный post-click reconcile");
    }

    private static async Task ProgressFaultAfterClick()
    {
        using var harness = await Harness.Create(Stock(("Inaprovaline", 5), ("Carbon", 5)));
        var throwOnce = 1;
        harness.Executor.ProgressChanged += progress =>
        {
            if (progress.State == ChemMasterExecutorState.WaitingForStateChange &&
                Interlocked.Exchange(ref throwOnce, 0) == 1)
                throw new InvalidOperationException("fake progress sink fault");
        };
        await harness.Executor.StartAsync("Bicaridine=10", ChemistryTargetMode.Ensure);
        Equal(harness.Executor.Progress.State, ChemMasterExecutorState.Failed, "Progress fault должен быть terminal после reconcile");
        Equal(harness.World.ClickCount, 1, "Progress fault повторил физический клик");
        Assert(harness.Executor.LastSnapshot != null &&
            SnapshotInventory.Sum(SnapshotInventory.From(harness.Executor.LastSnapshot).Beaker) == harness.World.Machine.Beaker.Volume,
            "Progress fault оборвал reconcile");
    }

    private static async Task ClickTelemetryFaultWithExternalChange()
    {
        using var harness = await Harness.Create(Stock(("Inaprovaline", 5), ("Carbon", 5)));
        var sentProgressCount = 0;
        harness.Executor.ProgressChanged += progress =>
        {
            if (progress.State != ChemMasterExecutorState.WaitingForStateChange) return;
            if (Interlocked.Increment(ref sentProgressCount) != 3) return;
            harness.World.Machine.Buffer.Add("Iron", 100);
            throw new InvalidOperationException("fake final-click progress sink fault");
        };
        var run = harness.Executor.StartAsync("Bicaridine=10", ChemistryTargetMode.Ensure);
        await WaitUntil(() => harness.Executor.IsExternalPause,
            "Внешнее изменение после click telemetry fault не потребовало решения");
        harness.Executor.AcceptExternalStateAndReplan();
        await run;
        Equal(harness.Executor.Progress.State, ChemMasterExecutorState.Failed,
            "Click telemetry fault позволил продолжить run после external reconcile");
        Equal(harness.World.ClickCount, 3, "Click telemetry fault + external change дали новый клик");
        Assert(harness.Executor.LastSnapshot?.Observation.CandidateSetComplete == true,
            "Click external reconcile не завершился полным snapshot");
    }

    private static async Task JournalFaultAfterWheelHint()
    {
        using var harness = await Harness.Create(HiddenBicaridineStock(), journalFactory:
            (root, directory) => new FaultingJournal(new ActionJournal(root, directory), "scroll-stable-hint"));
        await harness.Executor.StartAsync("Bicaridine=10", ChemistryTargetMode.Ensure);
        Equal(harness.Executor.Progress.State, ChemMasterExecutorState.Failed,
            "Fault журнала должен стать terminal только после полного scroll reconcile");
        Equal(harness.World.ScrollCount, 1, "Journal fault повторил wheel");
        Equal(harness.World.ClickCount, 0, "После journal fault появился reagent click");
        Assert(harness.World.FastReadCount >= 1, "Не было быстрого temporal read после wheel");
        Assert(harness.Executor.LastSnapshot?.Observation.CandidateSetComplete == true,
            "Journal fault оборвал обязательный полный snapshot после wheel");
    }

    private static async Task JournalFaultAfterScrollSent()
    {
        using var harness = await Harness.Create(HiddenBicaridineStock(), journalFactory:
            (root, directory) => new FaultingJournal(new ActionJournal(root, directory), "scroll-sent"));
        await harness.Executor.StartAsync("Bicaridine=10", ChemistryTargetMode.Ensure);
        Equal(harness.Executor.Progress.State, ChemMasterExecutorState.Failed,
            "Fault durable scroll-sent был потерян после reconcile");
        Equal(harness.World.ScrollCount, 1, "Fault scroll-sent повторил wheel");
        Equal(harness.World.ClickCount, 0, "После fault scroll-sent появился reagent click");
        Assert(harness.World.FastReadCount >= 1, "После fault scroll-sent не выполнен temporal reconcile");
        Assert(harness.Executor.LastSnapshot?.Observation.CandidateSetComplete == true,
            "Fault scroll-sent оборвал обязательный полный snapshot");
    }

    private static async Task ProgressFaultAfterWheelHint()
    {
        using var harness = await Harness.Create(HiddenBicaridineStock());
        var throwOnce = 1;
        harness.Executor.ProgressChanged += progress =>
        {
            if (progress.Message.StartsWith("Первое стабильное положение", StringComparison.Ordinal) &&
                Interlocked.Exchange(ref throwOnce, 0) == 1)
                throw new InvalidOperationException("fake scroll progress sink fault");
        };
        await harness.Executor.StartAsync("Bicaridine=10", ChemistryTargetMode.Ensure);
        Equal(harness.Executor.Progress.State, ChemMasterExecutorState.Failed,
            "Progress fault должен стать terminal только после полного scroll reconcile");
        Equal(harness.World.ScrollCount, 1, "Progress fault повторил wheel");
        Equal(harness.World.ClickCount, 0, "После progress fault появился reagent click");
        Assert(harness.World.FastReadCount >= 1, "Не было быстрого temporal read после wheel");
        Assert(harness.Executor.LastSnapshot?.Observation.CandidateSetComplete == true,
            "Progress fault оборвал обязательный полный snapshot после wheel");
    }

    private static async Task WheelTelemetryFaultWithExternalChange()
    {
        using var harness = await Harness.Create(HiddenBicaridineStock(), journalFactory:
            (root, directory) => new FaultingJournal(new ActionJournal(root, directory), "scroll-stable-hint"));
        harness.World.MutateChemistryOnNextFullAfterFast = true;
        var run = harness.Executor.StartAsync("Bicaridine=10", ChemistryTargetMode.Ensure);
        await WaitUntil(() => harness.Executor.IsExternalPause,
            "Внешнее изменение после wheel hint не потребовало решения");
        harness.Executor.AcceptExternalStateAndReplan();
        await run;
        Equal(harness.Executor.Progress.State, ChemMasterExecutorState.Failed,
            "Telemetry fault позволил продолжить run после external reconcile");
        Equal(harness.World.ScrollCount, 1, "Telemetry fault + external change повторили wheel");
        Equal(harness.World.ClickCount, 0, "После telemetry fault + external change появился reagent click");
        Assert(harness.Executor.LastSnapshot?.Observation.CandidateSetComplete == true,
            "External reconcile не завершился полным snapshot");
    }

    private static async Task IndeterminateInput()
    {
        using var harness = await Harness.Create(Stock(("Inaprovaline", 5), ("Carbon", 5)));
        harness.World.IndeterminateNextClick = true;
        harness.World.IndeterminateApplies = true;
        await harness.Executor.StartAsync("Bicaridine=10", ChemistryTargetMode.Ensure);
        Equal(harness.Executor.Progress.State, ChemMasterExecutorState.Failed, "Indeterminate input не должен продолжать run");
        Assert(harness.World.EmergencyStopped, "Indeterminate input не включил emergency latch");
        Equal(harness.World.ClickCount, 1, "Indeterminate input был повторён");
        Equal(harness.World.AppliedClickCount, 1, "Контрпример должен применить физический prefix");
        Assert(ReadJournal(harness.Executor.JournalPath).Contains("indeterminate-input-reconciled"),
            "Нет журнала reconcile неопределённого ввода");
    }

    private static async Task StaleFinalSnapshot()
    {
        using var harness = await Harness.Create(Stock(("Inaprovaline", 5), ("Carbon", 5)), configureSettings: settings =>
        {
            settings.SnapshotTimeoutMilliseconds = 6000;
            settings.MaximumSnapshotAgeMilliseconds = 5000;
        }, configureWorldBeforeConnect: world =>
            world.ObservedAtOffset = TimeSpan.FromMilliseconds(-5200));
        await harness.Executor.StartAsync("Bicaridine=10", ChemistryTargetMode.Ensure);
        Equal(harness.Executor.Progress.State, ChemMasterExecutorState.Failed, "Stale snapshot должен блокировать");
        Equal(harness.World.ClickCount, 0, "Commit использовал snapshot вне freshness budget");
        Assert(harness.Executor.Progress.Message.Contains("freshness", StringComparison.OrdinalIgnoreCase),
            "Нет причины freshness failure");
    }

    private static async Task LiveCaptureAgeAccepted()
    {
        using var harness = await Harness.Create(Stock(("Inaprovaline", 5), ("Carbon", 5)), configureSettings: settings =>
        {
            settings.SnapshotTimeoutMilliseconds = 6000;
            settings.MaximumSnapshotAgeMilliseconds = 5000;
        }, configureWorldBeforeConnect: world =>
            world.ObservedAtOffset = TimeSpan.FromMilliseconds(-2300));
        await harness.Executor.StartAsync("Bicaridine=10", ChemistryTargetMode.Ensure);
        Equal(harness.Executor.Progress.State, ChemMasterExecutorState.Completed, harness.Executor.Progress.Message);
    }

    private static Task FreshnessSettingBoundaries()
    {
        new AssistantSettings { MaximumSnapshotAgeMilliseconds = 2000 }.Validate();
        new AssistantSettings { MaximumSnapshotAgeMilliseconds = 10000 }.Validate();
        Throws<InvalidDataException>(() =>
        {
            new AssistantSettings { MaximumSnapshotAgeMilliseconds = 1999 }.Validate();
            return null;
        });
        Throws<InvalidDataException>(() =>
        {
            new AssistantSettings { MaximumSnapshotAgeMilliseconds = 10001 }.Validate();
            return null;
        });
        return Task.CompletedTask;
    }

    private static async Task DisposeWhileReading()
    {
        using var harness = await Harness.Create(Stock(("Inaprovaline", 5), ("Carbon", 5)));
        harness.World.IgnoreCancellationReadDelayMilliseconds = 300;
        var run = harness.Executor.StartAsync("Bicaridine=10", ChemistryTargetMode.Ensure);
        harness.Executor.Dispose();
        Assert(!harness.World.Disposed, "Dispose закрыл source до завершения активного ReadAsync");
        await run;
        await WaitUntil(() => harness.World.Disposed, "Deferred Dispose не закрыл source после run");
    }

    private static List<VirtualReagent> HiddenBicaridineStock()
    {
        var stock = Rules.Reagents.Keys
            .Where(id => id != "Inaprovaline" && id != "Carbon" && id != "Bicaridine")
            .Take(14)
            .Select(id => new VirtualReagent(id, 1))
            .ToList();
        stock.Add(new VirtualReagent("Inaprovaline", 5));
        stock.Add(new VirtualReagent("Carbon", 5));
        return stock;
    }

    private static async Task PreparedScrollPauseResumeRace()
    {
        CallbackJournal? callbackJournal = null;
        using var harness = await Harness.Create(HiddenBicaridineStock(), journalFactory: (root, directory) =>
            callbackJournal = new CallbackJournal(new ActionJournal(root, directory)));
        var fired = 0;
        callbackJournal!.OnWrite = eventName =>
        {
            if (eventName == "scroll-before" && Interlocked.Exchange(ref fired, 1) == 0)
            {
                harness.Executor.Pause();
                harness.Executor.Resume();
            }
        };
        await harness.Executor.StartAsync("Bicaridine=10", ChemistryTargetMode.Ensure);
        Equal(harness.Executor.Progress.State, ChemMasterExecutorState.Completed, harness.Executor.Progress.Message);
        Equal(fired, 1, "Race hook не сработал");
        var events = ReadJournal(harness.Executor.JournalPath);
        Assert(events.Count(name => name == "scroll-before") > harness.World.ScrollCount,
            "Старый control epoch не вызвал restart до wheel");
    }

    private static async Task PreparedScrollStaleUi()
    {
        CallbackJournal? callbackJournal = null;
        using var harness = await Harness.Create(HiddenBicaridineStock(), journalFactory: (root, directory) =>
            callbackJournal = new CallbackJournal(new ActionJournal(root, directory)));
        var fired = 0;
        callbackJournal!.OnWrite = eventName =>
        {
            if (eventName == "scroll-before" && Interlocked.Exchange(ref fired, 1) == 0)
                harness.World.MutateLastScrollSnapshot();
        };
        await harness.Executor.StartAsync("Bicaridine=10", ChemistryTargetMode.Ensure);
        Equal(harness.Executor.Progress.State, ChemMasterExecutorState.Completed, harness.Executor.Progress.Message);
        var events = ReadJournal(harness.Executor.JournalPath);
        Assert(events.Count(name => name == "scroll-before") > harness.World.ScrollCount,
            "Stale scroll snapshot дошёл до физического wheel");
    }

    private static async Task PreparedScrollStalePanel()
    {
        CallbackJournal? callbackJournal = null;
        using var harness = await Harness.Create(HiddenBicaridineStock(), journalFactory: (root, directory) =>
            callbackJournal = new CallbackJournal(new ActionJournal(root, directory)));
        callbackJournal!.OnWrite = eventName =>
        {
            if (eventName == "scroll-before") harness.World.MutateLastPanelSnapshot();
        };
        await harness.Executor.StartAsync("Bicaridine=10", ChemistryTargetMode.Ensure);
        Equal(harness.Executor.Progress.State, ChemMasterExecutorState.Failed, "Stale panel должен остановить run");
        Equal(harness.World.ScrollCount, 0, "Wheel был отправлен с устаревшей panel geometry");
        Equal(harness.World.ClickCount, 0, "После stale panel появился reagent click");
    }

    private static async Task IndeterminateScroll()
    {
        using var harness = await Harness.Create(HiddenBicaridineStock());
        harness.World.IndeterminateNextScroll = true;
        await harness.Executor.StartAsync("Bicaridine=10", ChemistryTargetMode.Ensure);
        Equal(harness.Executor.Progress.State, ChemMasterExecutorState.Failed, "Indeterminate wheel должен завершить run");
        Assert(harness.World.EmergencyStopped, "Indeterminate wheel не включил emergency latch");
        Equal(harness.World.ScrollCount, 1, "Indeterminate wheel был повторён");
        Equal(harness.World.ClickCount, 0, "После неопределённого wheel появился reagent click");
        Assert(ReadJournal(harness.Executor.JournalPath).Contains("indeterminate-scroll-reconciled"),
            "Нет bounded reconcile scroll/state");
    }

    private static async Task MisroutedScroll()
    {
        using var harness = await Harness.Create(HiddenBicaridineStock());
        harness.World.RouteNextScrollToOther = true;
        await harness.Executor.StartAsync("Bicaridine=10", ChemistryTargetMode.Ensure);
        Equal(harness.Executor.Progress.State, ChemMasterExecutorState.Failed,
            "Misrouted wheel должен завершить run");
        Assert(harness.World.EmergencyStopped, "Misrouted wheel не включил emergency latch");
        Equal(harness.World.ScrollCount, 1, "Misrouted wheel был повторён");
        Equal(harness.World.ClickCount, 0, "После misrouted wheel появился reagent click");
        Equal(harness.World.AppliedClickCount, 0, "Misrouted wheel изменил химию");
        Assert(ReadJournal(harness.Executor.JournalPath).Contains("wheel-route-mismatch"),
            "Нет журнала wheel route mismatch");
    }

    private static async Task BothScrollsMoved()
    {
        using var harness = await Harness.Create(HiddenBicaridineStock());
        harness.World.RouteNextScrollToOther = true;
        harness.World.AlsoMoveRequestedNextScroll = true;
        await harness.Executor.StartAsync("Bicaridine=10", ChemistryTargetMode.Ensure);
        Equal(harness.Executor.Progress.State, ChemMasterExecutorState.Failed,
            "Одновременный wrong/target scroll должен завершить run");
        Assert(harness.World.EmergencyStopped, "Одновременный scroll не включил emergency latch");
        Equal(harness.World.ScrollCount, 1, "Одновременный scroll был повторён");
        Equal(harness.World.ClickCount, 0, "После одновременного scroll появился reagent click");
    }

    private static async Task MissingPointerProof()
    {
        using var harness = await Harness.Create(Stock(("Inaprovaline", 5), ("Carbon", 5)),
            configureSettings: settings => settings.StableScrollTimeoutMilliseconds = 1000);
        harness.World.SuppressPointerProof = true;
        await harness.Executor.StartAsync("Bicaridine=10", ChemistryTargetMode.Ensure);
        Equal(harness.Executor.Progress.State, ChemMasterExecutorState.Failed,
            "Нет pointer proof должен завершить run");
        Equal(harness.World.ClickCount, 0, "Клик прошёл без pointer proof");
        Equal(harness.World.ScrollCount, 0, "Wheel прошёл без pointer proof");
        Assert(harness.World.PointerMoveCount > 0, "Pointer staging не выполнялся");
    }

    private static async Task HiddenScrollPhantomTarget()
    {
        using var harness = await Harness.Create(Stock(("Inaprovaline", 5), ("Carbon", 5)));
        harness.World.SetHiddenInputPhantomTarget();
        await harness.Executor.StartAsync("Bicaridine=10", ChemistryTargetMode.Ensure);
        Equal(harness.Executor.Progress.State, ChemMasterExecutorState.Completed,
            "Raw target невидимого scroll не должен блокировать точную кнопку");
        Assert(harness.World.Clicks.Any(click => !click.FromBuffer && click.Prototype == "Bicaridine"),
            "Не выполнен exact input return при невидимом scroll");
    }

    private static async Task ExternalDecisionStaleEpoch()
    {
        using var harness = await Harness.Create(Stock(("Inaprovaline", 5), ("Carbon", 5)));
        harness.World.PartialNextClick = true;
        var run = harness.Executor.StartAsync("Bicaridine=10", ChemistryTargetMode.Ensure);
        await WaitUntil(() => harness.Executor.IsExternalPause, "Нет первого external decision");
        var firstEpoch = harness.Executor.ExternalDecisionEpoch;
        harness.Executor.AcceptExternalStateAndReplan(firstEpoch);
        await WaitUntil(() => harness.Executor.IsExternalPause &&
            harness.Executor.ExternalDecisionEpoch > firstEpoch &&
            harness.Executor.Progress.Message.Contains("Мензурка не пуста", StringComparison.OrdinalIgnoreCase),
            "Не создан новый epoch после dirty validation");
        Throws<InvalidOperationException>(() =>
        {
            harness.Executor.AcceptExternalStateAndReplan(firstEpoch);
            return null;
        });
        harness.Executor.AbortExternalState(harness.Executor.ExternalDecisionEpoch);
        await run;
        Equal(harness.Executor.Progress.State, ChemMasterExecutorState.Aborted, "Abort нового epoch");
    }

    private static async Task ExternalAbortDuringValidation()
    {
        using var harness = await Harness.Create(Stock(("Inaprovaline", 5), ("Carbon", 5)));
        harness.World.OnRead(2, world => world.Machine.Buffer.Add("Iron", 100));
        var run = harness.Executor.StartAsync("Bicaridine=10", ChemistryTargetMode.Ensure);
        await WaitUntil(() => harness.Executor.IsExternalPause, "Нет external pause");
        var epoch = harness.Executor.ExternalDecisionEpoch;
        harness.World.ReadDelayMilliseconds = 200;
        harness.Executor.AcceptExternalStateAndReplan(epoch);
        harness.Executor.AbortExternalState(epoch);
        await run;
        Equal(harness.Executor.Progress.State, ChemMasterExecutorState.Aborted,
            "Abort между TCS и two-read validation был потерян");
        Equal(harness.World.ClickCount, 0, "После Abort validation появился click");
    }

    private static async Task ExternalAcceptanceRepeatedSequenceRejected()
    {
        using var harness = await Harness.Create(Stock(("Inaprovaline", 5), ("Carbon", 5)));
        harness.World.OnRead(2, world => world.Machine.Buffer.Add("Iron", 100));
        var run = harness.Executor.StartAsync("Bicaridine=10", ChemistryTargetMode.Ensure);
        await WaitUntil(() => harness.Executor.IsExternalPause, "Нет external pause");
        harness.World.RepeatNextSequence = true;
        harness.Executor.AcceptExternalStateAndReplan();
        await run;
        Equal(harness.Executor.Progress.State, ChemMasterExecutorState.Failed,
            "External accept использовал непричинный повторный snapshot");
        Equal(harness.World.ClickCount, 0, "После непричинного external accept появился click");
    }

    private static List<string> ReadJournal(string path)
    {
        var result = new List<string>();
        foreach (string line in ReadLinesShared(path))
        {
            using var document = JsonDocument.Parse(line);
            result.Add(document.RootElement.GetProperty("eventName").GetString()!);
        }
        return result;
    }

    private static string[] ReadLinesShared(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var lines = new List<string>();
        string? line;
        while ((line = reader.ReadLine()) != null) lines.Add(line);
        return lines.ToArray();
    }

    private static async Task WaitUntil(Func<bool> condition, string message, int timeoutMilliseconds = 5000)
    {
        var watch = Stopwatch.StartNew();
        while (watch.ElapsedMilliseconds < timeoutMilliseconds)
        {
            if (condition()) return;
            await Task.Delay(10).ConfigureAwait(false);
        }
        throw new Exception(message);
    }

    private static void AssertReadTelemetry(JsonElement read, bool expectedComplete, string expectedPath)
    {
        var snapshotMilliseconds = read.GetProperty("snapshotMilliseconds").GetDouble();
        var scanMilliseconds = read.GetProperty("scanMilliseconds").GetDouble();
        var totalReadMilliseconds = read.GetProperty("totalReadMilliseconds").GetDouble();
        Assert(double.IsFinite(snapshotMilliseconds) && snapshotMilliseconds >= 0,
            "Telemetry snapshotMilliseconds некорректна");
        Assert(double.IsFinite(scanMilliseconds) && scanMilliseconds >= 0,
            "Telemetry scanMilliseconds некорректна");
        Assert(double.IsFinite(totalReadMilliseconds) && totalReadMilliseconds >= 0 &&
            totalReadMilliseconds >= snapshotMilliseconds && totalReadMilliseconds >= scanMilliseconds,
            "Telemetry totalReadMilliseconds некорректна");
        Equal(read.GetProperty("candidateSetComplete").GetBoolean(), expectedComplete,
            "Telemetry candidateSetComplete");
        Equal(read.GetProperty("readPath").GetString(), expectedPath, "Telemetry readPath");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    private static void Equal<T>(T actual, T expected, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(actual, expected))
            throw new Exception($"{message}: expected {expected}, got {actual}");
    }

    private static void Throws<T>(Func<object?> action) where T : Exception
    {
        try { _ = action(); }
        catch (T) { return; }
        throw new Exception("Ожидалось исключение " + typeof(T).Name);
    }

    private sealed class Harness : IDisposable
    {
        public FakeChemMasterWorld World { get; }
        public ChemMasterExecutor Executor { get; }
        private readonly string _temporaryRoot;

        private Harness(FakeChemMasterWorld world, ChemMasterExecutor executor, string temporaryRoot)
        {
            World = world;
            Executor = executor;
            _temporaryRoot = temporaryRoot;
        }

        public static async Task<Harness> Create(List<VirtualReagent> stock, decimal capacity = 100,
            Func<string, string, IActionJournal>? journalFactory = null,
            Action<AssistantSettings>? configureSettings = null,
            Action<FakeChemMasterWorld>? configureWorldBeforeConnect = null)
        {
            string temporaryRoot = Path.Combine(Path.GetTempPath(), "ss14-chemmaster-executor-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temporaryRoot);
            var world = new FakeChemMasterWorld(stock, capacity);
            var calibration = new LiveCalibrationManager(Path.Combine(temporaryRoot, "live-calibration.json"));
            var settings = new AssistantSettings
            {
                SnapshotTimeoutMilliseconds = 1000,
                StateChangeTimeoutMilliseconds = 1000,
                StableScrollTimeoutMilliseconds = 1000,
                PollIntervalMilliseconds = 25,
                MaximumActions = 100,
                ActivateGameOnStart = false,
                LogDirectory = "logs",
            };
            configureSettings?.Invoke(settings);
            configureWorldBeforeConnect?.Invoke(world);
            var journal = journalFactory?.Invoke(temporaryRoot, settings.LogDirectory) ??
                new ActionJournal(temporaryRoot, settings.LogDirectory);
            var executor = new ChemMasterExecutor(world, world, calibration, settings, journal);
            await executor.ConnectAsync().ConfigureAwait(false);
            await executor.CalibrateCurrentAsync().ConfigureAwait(false);
            world.ResetScenarioCounters();
            return new Harness(world, executor, temporaryRoot);
        }

        public void Dispose()
        {
            Executor.Dispose();
            try { Directory.Delete(_temporaryRoot, true); } catch { }
        }
    }

    private sealed record ClickRecord(string Prototype, string Dose, bool FromBuffer, int ClientX, int ClientY, int WindowLeft);
    private sealed record ScrollFrame(double Value, double Target, bool Stable);

    private sealed class FaultingJournal : IActionJournal
    {
        private readonly IActionJournal _inner;
        private readonly string _eventName;
        private int _remaining = 1;
        public string Path => _inner.Path;

        public FaultingJournal(IActionJournal inner, string eventName)
        {
            _inner = inner;
            _eventName = eventName;
        }

        public void Write(string eventName, ChemMasterExecutorState state, object? payload = null)
        {
            if (eventName == _eventName && Interlocked.Exchange(ref _remaining, 0) == 1)
                throw new IOException("fake journal fault after physical click");
            _inner.Write(eventName, state, payload);
        }

        public void Dispose() => _inner.Dispose();
    }

    private sealed class CallbackJournal : IActionJournal
    {
        private readonly IActionJournal _inner;
        public Action<string>? OnWrite { get; set; }
        public string Path => _inner.Path;
        public CallbackJournal(IActionJournal inner) => _inner = inner;
        public void Write(string eventName, ChemMasterExecutorState state, object? payload = null)
        {
            OnWrite?.Invoke(eventName);
            _inner.Write(eventName, state, payload);
        }
        public void Dispose() => _inner.Dispose();
    }

    private sealed class FakeChemMasterWorld : IExecutorSnapshotSource, IGameInputDriver
    {
        private readonly object _sync = new();
        private readonly Dictionary<int, Action<FakeChemMasterWorld>> _readActions = new();
        private readonly Queue<ScrollFrame> _bufferScrollFrames = new();
        private readonly Queue<ScrollFrame> _inputScrollFrames = new();
        private ChemMasterWindowSnapshot? _staleState;
        private int _staleReadsRemaining;
        private long _sequence;
        private ChemMasterUiSnapshot? _lastUi;
        private double _bufferValue;
        private double _bufferTarget;
        private bool _bufferStable = true;
        private double _inputValue;
        private double _inputTarget;
        private bool _inputStable = true;
        private bool _lateStableTargetArmed;
        private int _pointerClientX = -1;
        private int _pointerClientY = -1;
        private string _hoveredScrollList = "";
        private bool _hoveredButtonValid;
        private string _hoveredButtonPrototype = "";
        private string _hoveredButtonDose = "";
        private bool _hoveredButtonFromBuffer;
        private bool _lastReadWasFast;
        private bool _forceFullInvalid;
        private bool _invalidFastArmed;
        private DateTimeOffset _lastObservedAt;
        private int _emergencyStopped;

        public VirtualChemMaster Machine { get; }
        public int ProcessId { get; } = 4242;
        public long WindowHandle { get; } = 0x434D4000;
        public bool WindowExists { get; set; } = true;
        public bool WindowActive { get; set; } = true;
        public bool InterfaceOpen { get; set; } = true;
        public bool SnapshotValid { get; set; } = true;
        public bool GeometryValid { get; set; } = true;
        public int ClientScreenX { get; set; } = 60;
        public int ClientScreenY { get; set; } = 90;
        public int ClientWidth { get; set; } = 1200;
        public int ClientHeight { get; set; } = 900;
        public int WindowLeft { get; set; } = 52;
        public int WindowTop { get; set; } = 60;
        public int WindowWidth { get; set; } = 1216;
        public int WindowHeight { get; set; } = 939;
        public uint Dpi { get; set; } = 96;
        public int ReadDelayMilliseconds { get; set; }
        public int IgnoreCancellationReadDelayMilliseconds { get; set; }
        public TimeSpan ObservedAtOffset { get; set; }
        public int StaleReadsAfterClick { get; set; }
        public bool SuppressClicks { get; set; }
        public bool PartialNextClick { get; set; }
        public bool BlockNextClick { get; set; }
        public bool IndeterminateNextClick { get; set; }
        public bool IndeterminateApplies { get; set; }
        public bool IndeterminateNextScroll { get; set; }
        public bool RouteNextScrollToOther { get; set; }
        public bool AlsoMoveRequestedNextScroll { get; set; }
        public bool SuppressPointerProof { get; set; }
        public bool DriftTargetAfterFirstStable { get; set; }
        public bool RepeatNextSequence { get; set; }
        public bool MutateChemistryOnNextFullAfterFast { get; set; }
        public bool FastReadsFallbackToComplete { get; set; }
        public bool InvalidateFastReadsUntilFullControl { get; set; }
        public bool InvalidateFullControlAfterFast { get; set; }
        public bool RegressTimestampOnNextFullAfterFast { get; set; }
        public bool ActivationSucceeds { get; set; } = true;
        public bool ActivationSetsWindowActive { get; set; } = true;
        public int ReadCount { get; private set; }
        public int FullReadCount { get; private set; }
        public int FastReadCount { get; private set; }
        public int ActivationCallCount { get; private set; }
        public int ClickCount { get; private set; }
        public int AppliedClickCount { get; private set; }
        public int PointerMoveCount { get; private set; }
        public int ScrollCount { get; private set; }
        public int ScrollTargetChangeCount { get; private set; }
        public List<ClickRecord> Clicks { get; } = new();
        public ManualResetEventSlim ClickEntered { get; } = new(false);
        public ManualResetEventSlim ReleaseClick { get; } = new(false);
        public bool Disposed { get; private set; }
        public bool EmergencyStopped => Volatile.Read(ref _emergencyStopped) != 0;

        public FakeChemMasterWorld(List<VirtualReagent> stock, decimal capacity)
        {
            Machine = new VirtualChemMaster(Rules, stock, capacity, null, Names);
        }

        public void ResetScenarioCounters()
        {
            lock (_sync)
            {
                ReadCount = FullReadCount = FastReadCount = ActivationCallCount =
                    ClickCount = AppliedClickCount = PointerMoveCount = ScrollCount =
                    ScrollTargetChangeCount = 0;
                Clicks.Clear();
                _readActions.Clear();
                _bufferScrollFrames.Clear();
                _inputScrollFrames.Clear();
                _staleState = null;
                _staleReadsRemaining = 0;
                _lateStableTargetArmed = false;
                DriftTargetAfterFirstStable = false;
                RepeatNextSequence = false;
                MutateChemistryOnNextFullAfterFast = false;
                FastReadsFallbackToComplete = false;
                InvalidateFastReadsUntilFullControl = false;
                InvalidateFullControlAfterFast = false;
                RegressTimestampOnNextFullAfterFast = false;
                _lastReadWasFast = false;
                _forceFullInvalid = false;
                _invalidFastArmed = false;
                _lastObservedAt = default;
                ClickEntered.Reset();
                ReleaseClick.Reset();
            }
        }

        public void OnRead(int readNumber, Action<FakeChemMasterWorld> action)
        {
            lock (_sync) _readActions.Add(readNumber, action);
        }

        public void QueueVisibleMovingBufferScroll()
        {
            _bufferScrollFrames.Enqueue(new ScrollFrame(0.4, 0, false));
            _bufferScrollFrames.Enqueue(new ScrollFrame(0, 0, true));
            _bufferScrollFrames.Enqueue(new ScrollFrame(0, 0, true));
        }

        public void SetHiddenInputPhantomTarget()
        {
            lock (_sync)
            {
                _inputValue = 0;
                _inputTarget = 50;
                _inputStable = false;
            }
        }

        public void MutateLastScrollSnapshot()
        {
            lock (_sync)
            {
                if (_lastUi == null) throw new InvalidOperationException("Нет UI для stale mutation.");
                _lastUi.BufferScroll.Target += 0.25;
                _lastUi.BufferScroll.Stable = false;
            }
        }

        public void MutateLastPanelSnapshot()
        {
            lock (_sync)
            {
                if (_lastUi == null) throw new InvalidOperationException("Нет UI для stale panel mutation.");
                _lastUi.PanelBounds.X++;
            }
        }

        public void LoseWindowFocus()
        {
            lock (_sync) WindowActive = false;
        }

        public void MovePhysicalPointerAway()
        {
            lock (_sync)
            {
                _pointerClientX = -1;
                _pointerClientY = -1;
                _hoveredScrollList = "";
                _hoveredButtonValid = false;
                _hoveredButtonPrototype = "";
                _hoveredButtonDose = "";
                _hoveredButtonFromBuffer = false;
            }
        }

        public Task<ExecutorSnapshot> ReadAsync(CancellationToken cancellationToken)
        {
            lock (_sync) FullReadCount++;
            return ReadCoreAsync(cancellationToken, candidateSetComplete: true, requestedFast: false);
        }

        public Task<ExecutorSnapshot> ReadFastAsync(CancellationToken cancellationToken)
        {
            lock (_sync) FastReadCount++;
            return ReadCoreAsync(cancellationToken, candidateSetComplete: FastReadsFallbackToComplete,
                requestedFast: true);
        }

        private async Task<ExecutorSnapshot> ReadCoreAsync(CancellationToken cancellationToken,
            bool candidateSetComplete, bool requestedFast)
        {
            int uncancellableDelay = IgnoreCancellationReadDelayMilliseconds;
            if (uncancellableDelay > 0) await Task.Delay(uncancellableDelay).ConfigureAwait(false);
            int delay = ReadDelayMilliseconds;
            if (delay > 0) await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            lock (_sync)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var fullAfterFast = !requestedFast && _lastReadWasFast;
                if (fullAfterFast && InvalidateFullControlAfterFast) _forceFullInvalid = true;
                if (requestedFast && InvalidateFastReadsUntilFullControl) _invalidFastArmed = true;
                if (!requestedFast && _invalidFastArmed)
                {
                    InvalidateFastReadsUntilFullControl = false;
                    _invalidFastArmed = false;
                }
                var invalidateState = !requestedFast && _forceFullInvalid ||
                    requestedFast && InvalidateFastReadsUntilFullControl;
                var regressTimestamp = fullAfterFast && RegressTimestampOnNextFullAfterFast;
                if (fullAfterFast && MutateChemistryOnNextFullAfterFast)
                {
                    Machine.Buffer.Add("Carbon", 100);
                    MutateChemistryOnNextFullAfterFast = false;
                }
                if (regressTimestamp) RegressTimestampOnNextFullAfterFast = false;
                _lastReadWasFast = requestedFast;
                ReadCount++;
                if (_readActions.Remove(ReadCount, out var action)) action(this);
                AdvanceScroll();
                if (DriftTargetAfterFirstStable && ScrollCount > 0 && _bufferStable &&
                    Math.Abs(_bufferValue - _bufferTarget) <= 0.01)
                {
                    if (!_lateStableTargetArmed)
                    {
                        _lateStableTargetArmed = true;
                    }
                    else
                    {
                        var changedTarget = _bufferTarget > 0 ? _bufferTarget - 0.5 : _bufferTarget + 0.5;
                        _bufferValue = _bufferTarget = changedTarget;
                        DriftTargetAfterFirstStable = false;
                    }
                }
                var state = invalidateState
                    ? ChemMasterWindowSnapshot.Invalid("Найдено несколько активных окон ChemMaster.")
                    : _staleReadsRemaining > 0 && _staleState != null ? _staleState : BuildState();
                if (_staleReadsRemaining > 0) _staleReadsRemaining--;
                var window = BuildWindow();
                var observedAt = regressTimestamp && _lastObservedAt != default
                    ? _lastObservedAt
                    : DateTimeOffset.UtcNow + ObservedAtOffset;
                _lastObservedAt = observedAt;
                var readPath = requestedFast
                    ? candidateSetComplete ? "fast-fallback-full" : "fast-cache-hit"
                    : "full";
                var observation = new ChemMasterObservation(observedAt, ProcessId, 1, 1, 2, readPath,
                    state, candidateSetComplete);
                var sequence = RepeatNextSequence ? _sequence : ++_sequence;
                RepeatNextSequence = false;
                return new ExecutorSnapshot(sequence, observedAt, observation, window);
            }
        }

        public void SetEmergencyStop() => Interlocked.Exchange(ref _emergencyStopped, 1);
        public void ResetEmergencyStop() => Interlocked.Exchange(ref _emergencyStopped, 0);

        public bool TryActivate()
        {
            lock (_sync)
            {
                ActivationCallCount++;
                if (EmergencyStopped || !WindowExists || !ActivationSucceeds) return false;
                if (ActivationSetsWindowActive) WindowActive = true;
                return true;
            }
        }

        public void Click(GameWindowSnapshot expectedWindow, ChemMasterUiRect panel, int clientX, int clientY)
        {
            lock (_sync)
            {
                ValidateInput(expectedWindow, panel, clientX, clientY);
                var hit = FindButton(clientX, clientY);
                if (_pointerClientX != clientX || _pointerClientY != clientY || !_hoveredButtonValid ||
                    !StringComparer.Ordinal.Equals(_hoveredButtonPrototype, hit.Row.Prototype) ||
                    !StringComparer.Ordinal.Equals(_hoveredButtonDose, hit.Dose) ||
                    _hoveredButtonFromBuffer != hit.FromBuffer)
                    throw new InvalidOperationException("Fake click не получил подтверждённый pointer/hover кнопки.");
                ClickCount++;
                Clicks.Add(new ClickRecord(hit.Row.Prototype, hit.Dose, hit.FromBuffer, clientX, clientY, expectedWindow.WindowLeft));
                var stale = BuildState();
                if (BlockNextClick)
                {
                    BlockNextClick = false;
                    ClickEntered.Set();
                    ReleaseClick.Wait();
                }
                if (SuppressClicks) return;
                if (IndeterminateNextClick && !IndeterminateApplies)
                {
                    IndeterminateNextClick = false;
                    throw new IndeterminateGameInputException(5, 2, 3, true, true);
                }
                string appliedDose = PartialNextClick && hit.Dose != "1" ? "1" : hit.Dose;
                PartialNextClick = false;
                Machine.Apply(Machine.Prepare(hit.Row.Prototype, appliedDose, hit.FromBuffer));
                AppliedClickCount++;
                if (StaleReadsAfterClick > 0)
                {
                    _staleState = stale;
                    _staleReadsRemaining = StaleReadsAfterClick;
                }
                if (IndeterminateNextClick)
                {
                    IndeterminateNextClick = false;
                    throw new IndeterminateGameInputException(5, 2, 3, true, true);
                }
            }
        }

        public void MovePointer(GameWindowSnapshot expectedWindow, ChemMasterUiRect panel,
            int clientX, int clientY)
        {
            lock (_sync)
            {
                ValidateInput(expectedWindow, panel, clientX, clientY);
                if (_lastUi == null) throw new InvalidOperationException("Нет UI для pointer move.");
                _pointerClientX = clientX;
                _pointerClientY = clientY;
                _hoveredButtonValid = false;
                _hoveredButtonPrototype = "";
                _hoveredButtonDose = "";
                _hoveredButtonFromBuffer = false;
                if (SuppressPointerProof)
                    _hoveredScrollList = "";
                else if (_lastUi.BufferScrollBarBounds.Contains(clientX, clientY))
                    _hoveredScrollList = "buffer";
                else if (_lastUi.InputScrollBarBounds.Contains(clientX, clientY))
                    _hoveredScrollList = "input";
                else
                {
                    var hit = FindButton(clientX, clientY);
                    _hoveredScrollList = hit.FromBuffer ? "buffer" : "input";
                    _hoveredButtonValid = true;
                    _hoveredButtonPrototype = hit.Row.Prototype;
                    _hoveredButtonDose = hit.Dose;
                    _hoveredButtonFromBuffer = hit.FromBuffer;
                }
                PointerMoveCount++;
            }
        }

        public void Scroll(GameWindowSnapshot expectedWindow, ChemMasterUiRect panel, int clientX, int clientY, int wheelDelta)
        {
            lock (_sync)
            {
                ValidateInput(expectedWindow, panel, clientX, clientY);
                if (_lastUi == null) throw new InvalidOperationException("Нет UI для scroll.");
                bool buffer = _lastUi.BufferScrollBarBounds.Contains(clientX, clientY);
                bool input = _lastUi.InputScrollBarBounds.Contains(clientX, clientY);
                if (buffer == input) throw new InvalidOperationException("Scroll вне ровно одного viewport.");
                if (_pointerClientX != clientX || _pointerClientY != clientY ||
                    !StringComparer.Ordinal.Equals(_hoveredScrollList, buffer ? "buffer" : "input"))
                    throw new InvalidOperationException("Fake wheel не получил подтверждённый pointer/hover списка.");
                ScrollCount++;
                var requestedBuffer = buffer;
                var actualBuffer = RouteNextScrollToOther ? !requestedBuffer : requestedBuffer;
                QueueScroll(actualBuffer, wheelDelta);
                if (AlsoMoveRequestedNextScroll && actualBuffer != requestedBuffer)
                    QueueScroll(requestedBuffer, wheelDelta);
                RouteNextScrollToOther = false;
                AlsoMoveRequestedNextScroll = false;
                ScrollTargetChangeCount++;
                if (IndeterminateNextScroll)
                {
                    IndeterminateNextScroll = false;
                    throw new IndeterminateGameInputException(5, 1, 2, false, true);
                }
            }
        }

        private void QueueScroll(bool buffer, int wheelDelta)
        {
            if (_lastUi == null) throw new InvalidOperationException("Нет UI для scroll queue.");
            int count = buffer ? _lastUi.BufferRows.Count : _lastUi.InputRows.Count;
            double current = buffer ? _bufferValue : _inputValue;
            var queue = buffer ? _bufferScrollFrames : _inputScrollFrames;
            if (count <= 5)
            {
                // Live Robust leaves an invisible bar at Value=0 while wheel can
                // still change its raw ValueTarget (the observed 0/50 mismatch).
                var hiddenTarget = wheelDelta < 0 ? Math.Min(100, current + 50) : Math.Max(0, current - 50);
                queue.Clear();
                for (var index = 0; index < 4; index++)
                    queue.Enqueue(new ScrollFrame(current, hiddenTarget, false));
                return;
            }
            double finalTarget = wheelDelta < 0 ? Math.Max(0, count - 5) : 0;
            double firstTarget = current + (finalTarget - current) * 0.65;
            queue.Clear();
            queue.Enqueue(new ScrollFrame(current + (firstTarget - current) * 0.45, firstTarget, false));
            queue.Enqueue(new ScrollFrame(current + (finalTarget - current) * 0.72, finalTarget, false));
            queue.Enqueue(new ScrollFrame(finalTarget, finalTarget, true));
            queue.Enqueue(new ScrollFrame(finalTarget, finalTarget, true));
        }

        private void AdvanceScroll()
        {
            if (_bufferScrollFrames.Count > 0)
            {
                var frame = _bufferScrollFrames.Dequeue();
                _bufferValue = frame.Value; _bufferTarget = frame.Target; _bufferStable = frame.Stable;
            }
            if (_inputScrollFrames.Count > 0)
            {
                var frame = _inputScrollFrames.Dequeue();
                _inputValue = frame.Value; _inputTarget = frame.Target; _inputStable = frame.Stable;
            }
        }

        private void ValidateInput(GameWindowSnapshot expected, ChemMasterUiRect panel, int clientX, int clientY)
        {
            if (EmergencyStopped) throw new OperationCanceledException("Emergency stop");
            var current = BuildWindow();
            if (!current.Exists || !current.Active || expected.Handle != current.Handle || expected.ProcessId != current.ProcessId ||
                expected.ClientWidth != current.ClientWidth || expected.ClientHeight != current.ClientHeight || expected.Dpi != current.Dpi)
                throw new InvalidOperationException("Fake input rejected stale/inactive window.");
            if (_lastUi == null || !Same(panel, _lastUi.PanelBounds) || !panel.Contains(clientX, clientY))
                throw new InvalidOperationException("Fake input rejected point outside current panel.");
        }

        private (ChemMasterUiRow Row, string Dose, bool FromBuffer) FindButton(int x, int y)
        {
            if (_lastUi == null) throw new InvalidOperationException("Нет UI.");
            foreach (var pair in new[] { (_lastUi.BufferRows, true), (_lastUi.InputRows, false) })
                foreach (var row in pair.Item1)
                    foreach (var button in row.DoseButtons)
                        if (button.Value.Contains(x, y)) return (row, button.Key, pair.Item2);
            throw new InvalidOperationException("Координата не попала в кнопку.");
        }

        private GameWindowSnapshot BuildWindow() => new(WindowHandle, ProcessId, WindowExists, WindowActive,
            ClientScreenX, ClientScreenY, ClientWidth, ClientHeight, WindowLeft, WindowTop, WindowWidth, WindowHeight, Dpi);

        private ChemMasterWindowSnapshot BuildState()
        {
            if (!InterfaceOpen) return ChemMasterWindowSnapshot.Closed;
            if (!SnapshotValid) return ChemMasterWindowSnapshot.Invalid("fake invalid snapshot");
            var ui = BuildUi();
            var buffer = Machine.Buffer.Items.Select((row, index) =>
                new ChemMasterReagentAmount(index, row.Prototype, row.Amount)).ToList();
            var beaker = Machine.Beaker.Items.Select((row, index) =>
                new ChemMasterReagentAmount(index, row.Prototype, row.Amount)).ToList();
            var raw = new ChemMasterRawSnapshot(
                Machine.Mode == "transfer" ? 0 : 1,
                Machine.Sorting switch { "none" => (byte) 0, "alphabetical" => (byte) 1, "quantity" => (byte) 2, "latest" => (byte) 3, _ => (byte) 255 },
                Machine.Buffer.Volume,
                0, 50, false,
                new ChemMasterContainerSnapshot("beaker", Machine.Beaker.Volume, Machine.Capacity, true, beaker, 1),
                null,
                buffer);
            return ChemMasterWindowSnapshot.Valid(raw, ui);
        }

        private ChemMasterUiSnapshot BuildUi()
        {
            var virtualUi = Machine.Ui();
            var panel = new ChemMasterUiRect { X = 100, Y = 70, Width = 900, Height = 720 };
            var inputViewport = new ChemMasterUiRect { X = 125, Y = 105, Width = 820, Height = 160 };
            var bufferViewport = new ChemMasterUiRect { X = 125, Y = 390, Width = 820, Height = 160 };
            var inputScrollBar = new ChemMasterUiRect { X = 945, Y = 105, Width = 20, Height = 160 };
            var bufferScrollBar = new ChemMasterUiRect { X = 945, Y = 390, Width = 20, Height = 160 };
            ClampScroll(virtualUi.BufferRows.Count, ref _bufferValue, ref _bufferTarget);
            ClampScroll(virtualUi.InputRows.Count, ref _inputValue, ref _inputTarget);
            var ui = new ChemMasterUiSnapshot
            {
                Source = "live-ui-controls",
                RowOrderValid = true,
                GeometryValid = GeometryValid,
                UiScale = 1,
                PanelBounds = panel,
                InputViewportBounds = inputViewport,
                BufferViewportBounds = bufferViewport,
                InputScrollBarBounds = virtualUi.InputRows.Count > 5 ? inputScrollBar : new ChemMasterUiRect(),
                BufferScrollBarBounds = virtualUi.BufferRows.Count > 5 ? bufferScrollBar : new ChemMasterUiRect(),
                PointerClientX = _pointerClientX,
                PointerClientY = _pointerClientY,
                PointerFramebufferWidth = ClientWidth,
                PointerFramebufferHeight = ClientHeight,
                PointerStateValid = _pointerClientX >= 0 && _pointerClientY >= 0,
                HoveredScrollList = _hoveredScrollList,
                HoveredButtonValid = _hoveredButtonValid,
                HoveredButtonPrototype = _hoveredButtonPrototype,
                HoveredButtonDose = _hoveredButtonDose,
                HoveredButtonFromBuffer = _hoveredButtonFromBuffer,
                BufferRows = BuildRows(virtualUi.BufferRows, bufferViewport, (int) Math.Round(_bufferValue)),
                InputRows = BuildRows(virtualUi.InputRows, inputViewport, (int) Math.Round(_inputValue)),
                BufferScroll = new ChemMasterScrollState
                {
                    Value = _bufferValue, Target = _bufferTarget, Stable = _bufferStable,
                    Page = virtualUi.BufferRows.Count > 5 ? 5 : 0,
                    Maximum = virtualUi.BufferRows.Count > 5 ? Math.Max(0, virtualUi.BufferRows.Count - 5) : 100,
                    Visible = virtualUi.BufferRows.Count > 5,
                },
                InputScroll = new ChemMasterScrollState
                {
                    Value = _inputValue, Target = _inputTarget, Stable = _inputStable,
                    Page = virtualUi.InputRows.Count > 5 ? 5 : 0,
                    Maximum = virtualUi.InputRows.Count > 5 ? Math.Max(0, virtualUi.InputRows.Count - 5) : 100,
                    Visible = virtualUi.InputRows.Count > 5,
                },
            };
            _lastUi = ui;
            return ui;
        }

        private static void ClampScroll(int count, ref double value, ref double target)
        {
            double maximum = count > 5 ? count - 5 : 100;
            value = Math.Clamp(value, 0, maximum);
            target = Math.Clamp(target, 0, maximum);
        }

        private static List<ChemMasterUiRow> BuildRows(List<ChemMasterUiRow> rows, ChemMasterUiRect viewport, int first)
        {
            var result = new List<ChemMasterUiRow>();
            for (var index = 0; index < rows.Count; index++)
            {
                var row = new ChemMasterUiRow(index, rows[index].Prototype);
                int y = viewport.Y + 5 + (index - first) * 30;
                for (var doseIndex = 0; doseIndex < ChemCalibration.Doses.Length; doseIndex++)
                    row.DoseButtons[ChemCalibration.Doses[doseIndex]] = new ChemMasterUiRect
                    {
                        X = viewport.X + 5 + doseIndex * 72,
                        Y = y,
                        Width = 62,
                        Height = 20,
                    };
                result.Add(row);
            }
            return result;
        }

        private static bool Same(ChemMasterUiRect left, ChemMasterUiRect right) =>
            left.X == right.X && left.Y == right.Y && left.Width == right.Width && left.Height == right.Height;

        public void Dispose()
        {
            Disposed = true;
            ClickEntered.Dispose();
            ReleaseClick.Dispose();
        }
    }
}
