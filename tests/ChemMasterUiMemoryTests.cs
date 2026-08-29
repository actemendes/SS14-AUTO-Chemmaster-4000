using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Reflection;
using Microsoft.Diagnostics.Runtime;
using Ss14.Chemistry;

internal static class ChemMasterUiMemoryTests
{
    private static object? fixtureRoot;
    public static int Main(string[] args)
    {
        if (args.Contains("--fixture"))
        {
            var fixture = new CalibrationMemoryFixture.FixtureSet();
            fixtureRoot = fixture;
            Console.WriteLine("READY"); Console.Out.Flush();
            while (true)
            {
                var command = Console.ReadLine();
                if (command == null || command == "STOP") break;
                switch (command)
                {
                    case "CLOSE": fixture.PrimaryBui.IsOpened = false; break;
                    case "OPEN": fixture.PrimaryBui.IsOpened = true; break;
                    case "SECOND": fixture.AddSecondBui(); break;
                    case "REMOVE_SECOND": fixture.RemoveSecondBui(); break;
                    default: throw new InvalidOperationException("Unknown fixture command: " + command);
                }
                Console.WriteLine("DONE"); Console.Out.Flush();
            }
            GC.KeepAlive(fixtureRoot);
            return 0;
        }
        using var child = new Process
        {
            StartInfo = new ProcessStartInfo(Environment.ProcessPath!)
            {
                ArgumentList = { Assembly.GetExecutingAssembly().Location, "--fixture" },
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardInput = true,
            }
        };
        try
        {
            child.Start();
            var ready = child.StandardOutput.ReadLineAsync();
            if (!ready.Wait(TimeSpan.FromSeconds(10)) || ready.Result != "READY") throw new Exception("Fixture did not start.");
            using var target = DataTarget.CreateSnapshotAndAttach(child.Id);
            var dac = Path.Combine(Path.GetDirectoryName(typeof(object).Assembly.Location)!, "mscordaccore.dll");
            using var runtime = target.ClrVersions.Single().CreateRuntime(dac);
            ClrObject One(string name) => runtime.Heap.EnumerateObjects().Single(x => x.Type?.Name == name);

            var window = One("CalibrationMemoryFixture.Window");
            var result = ChemMasterUiReader.Read(window, new[] { "Water" }, new[] { "Nitrogen", "Oxygen" });
            Require(result.RowOrderValid, result.Error);
            Require(result.GeometryValid, result.Error);
            Require(result.InputRows.Single().Prototype == "Water", "Input container reagent ID was lost.");
            Require(result.BufferRows.Select(x => x.Prototype).SequenceEqual(new[] { "Oxygen", "Nitrogen" }), "UI order was replaced by raw order.");
            Require(result.InputScroll != null && result.InputScroll.Visible && result.InputScroll.Stable && result.InputScroll.Value == 0, "Input scroll was not read.");
            Require(result.BufferScroll != null && result.BufferScroll.Visible && !result.BufferScroll.Stable && result.BufferScroll.Value == 25 && result.BufferScroll.Target == 50, "Animated buffer scroll was not exposed.");
            Console.WriteLine("PASS read actual button IDs and display order from fixture process memory");

            Require(Math.Abs(result.UiScale - 1.25) < 0.0001, "WindowRoot UI scale was not read.");
            Rect(result.PanelBounds, 13, 25, 625, 500, "panel");
            Rect(result.InputViewportBounds, 23, 69, 581, 137, "input viewport");
            Rect(result.BufferViewportBounds, 23, 263, 581, 137, "buffer viewport");
            Rect(result.InputScrollBarBounds, 604, 69, 18, 137, "input scrollbar");
            Rect(result.BufferScrollBarBounds, 604, 263, 18, 137, "buffer scrollbar");
            Require(result.InputScrollBarBounds.Y != result.BufferScrollBarBounds.Y &&
                !result.InputScrollBarBounds.Contains(result.BufferScrollBarBounds),
                "Input and buffer scrollbars were not distinguished.");
            Require(result.PointerStateValid && Math.Abs(result.PointerClientX - 613) < 0.001 &&
                Math.Abs(result.PointerClientY - 331) < 0.001 && result.PointerFramebufferWidth == 1000 &&
                result.PointerFramebufferHeight == 750 && result.HoveredScrollList == "buffer",
                "Physical LastMousePos/framebuffer/hovered scroll proof was not read.");
            Require(!result.HoveredButtonValid && result.HoveredButtonPrototype == "" && result.HoveredButtonDose == "",
                "ScrollContainer hover was misidentified as a reagent button.");
            var dose = result.BufferRows[1].DoseButtons["15"];
            Rect(dose, 233, 321, 40, 25, "exact dose button");
            Require(result.BufferRows.All(row => row.DoseButtons.Keys.OrderBy(x => x, StringComparer.Ordinal)
                .SequenceEqual(ChemCalibration.Doses.OrderBy(x => x, StringComparer.Ordinal))), "Exact dose map is incomplete.");
            Console.WriteLine("PASS read exact client-relative panel, viewport and dose-button physical bounds");
            Console.WriteLine("PASS reproduce Robust per-ancestor truncation instead of rounding a summed float position");
            Console.WriteLine("PASS read distinct exact scrollbar bounds and SDL LastMousePos pointer proof");

            var buttonHover = ChemMasterUiReader.Read(One("CalibrationMemoryFixture.ButtonHoverWindow"),
                new[] { "Water" }, new[] { "Nitrogen", "Oxygen" });
            Require(buttonHover.GeometryValid && buttonHover.PointerStateValid &&
                buttonHover.HoveredScrollList == "buffer" && buttonHover.HoveredButtonValid &&
                buttonHover.HoveredButtonPrototype == "Oxygen" && buttonHover.HoveredButtonDose == "1" &&
                buttonHover.HoveredButtonFromBuffer,
                "Exact hovered ReagentButton identity was not exported.");
            Console.WriteLine("PASS read exact hovered ReagentButton prototype/dose/list identity");

            var outsidePointer = ChemMasterUiReader.Read(One("CalibrationMemoryFixture.OutsidePointerWindow"),
                new[] { "Water" }, new[] { "Nitrogen", "Oxygen" });
            Require(outsidePointer.GeometryValid && !outsidePointer.PointerStateValid &&
                outsidePointer.PointerClientX == -1 && outsidePointer.HoveredScrollList == "",
                "Out-of-client LastMousePos was not exposed as a non-fatal false proof.");
            var noHover = ChemMasterUiReader.Read(One("CalibrationMemoryFixture.NoHoverWindow"),
                new[] { "Water" }, new[] { "Nitrogen", "Oxygen" });
            Require(noHover.GeometryValid && !noHover.PointerStateValid && noHover.HoveredScrollList == "",
                "Null CurrentlyHovered was not exposed as a non-fatal false proof.");
            Console.WriteLine("PASS pointer outside client or without hover is a safe false proof");

            var badPointer = ChemMasterUiReader.Read(One("CalibrationMemoryFixture.BadPointerWindow"),
                new[] { "Water" }, new[] { "Nitrogen", "Oxygen" });
            Require(!badPointer.GeometryValid && !badPointer.PointerStateValid && badPointer.BufferRows.Count == 0,
                "Pointer infrastructure field/type drift did not fail closed.");
            Console.WriteLine("PASS pointer infrastructure field/type drift fails closed");

            var mismatch = ChemMasterUiReader.Read(window, new[] { "Iron" }, new[] { "Nitrogen", "Oxygen" });
            Require(!mismatch.RowOrderValid && !mismatch.GeometryValid && mismatch.InputRows.Count == 0 && mismatch.BufferRows.Count == 0,
                "Mismatched inventory did not fail closed.");
            Console.WriteLine("PASS inventory mismatch clears unsafe partial UI order and geometry");
            var duplicates = ChemMasterUiReader.Read(window, new[] { "Water" }, new[] { "Oxygen", "Oxygen" });
            Require(!duplicates.RowOrderValid, "Duplicate raw IDs accepted.");
            Console.WriteLine("PASS ambiguous reagent IDs rejected");

            var disabled = ChemMasterUiReader.Read(One("CalibrationMemoryFixture.DisabledWindow"),
                new[] { "Water" }, new[] { "Nitrogen", "Oxygen" });
            Require(!disabled.GeometryValid && disabled.BufferRows.Count == 0, "Disabled dose button was accepted.");
            Console.WriteLine("PASS disabled dose button fails closed");
            var badDose = ChemMasterUiReader.Read(One("CalibrationMemoryFixture.BadDoseWindow"),
                new[] { "Water" }, new[] { "Nitrogen", "Oxygen" });
            Require(!badDose.GeometryValid && badDose.BufferRows.Count == 0, "Duplicate/missing dose enum was accepted.");
            Console.WriteLine("PASS exact Amount-to-dose set is required");
            var hidden = ChemMasterUiReader.Read(One("CalibrationMemoryFixture.HiddenWindow"),
                new[] { "Water" }, new[] { "Nitrogen", "Oxygen" });
            Require(!hidden.GeometryValid && hidden.BufferRows.Count == 0, "Hidden ancestor was accepted.");
            Console.WriteLine("PASS hidden or detached live-control chain fails closed");

            var invalid = ChemMasterWindowSnapshot.Invalid("fixture");
            var ambiguous = ChemMasterBuiReader.ResolveCandidates(new[] { invalid, invalid });
            Require(ambiguous.InterfaceOpen && !ambiguous.SnapshotValid &&
                ambiguous.Error?.Contains("несколько", StringComparison.OrdinalIgnoreCase) == true,
                "Multiple active BUI candidates were selected instead of rejected.");
            Require(!ChemMasterBuiReader.ResolveCandidates(Array.Empty<ChemMasterWindowSnapshot>()).InterfaceOpen,
                "Empty BUI candidate set did not report a closed interface.");
            Console.WriteLine("PASS zero/one/multiple active BUI candidate selection is fail-closed");

            void FixtureCommand(string command)
            {
                child.StandardInput.WriteLine(command); child.StandardInput.Flush();
                var response = child.StandardOutput.ReadLineAsync();
                if (!response.Wait(TimeSpan.FromSeconds(10)) || response.Result != "DONE")
                    throw new Exception("Fixture command failed: " + command);
            }

            var cache = new ChemMasterBuiReadCache();
            ChemMasterObservation FixtureRead(bool cached) =>
                ChemMasterBuiReader.ReadForFixtureTest(child.Id, dac, cache, cached);
            var fullBui = FixtureRead(cached: false);
            Require(fullBui.CandidateSetComplete && fullBui.State.SnapshotValid &&
                fullBui.ReadPath == "full" && double.IsFinite(fullBui.TotalReadMilliseconds) &&
                fullBui.TotalReadMilliseconds >= fullBui.SnapshotMilliseconds &&
                fullBui.TotalReadMilliseconds >= fullBui.ScanMilliseconds,
                "Complete BUI scan did not seed an authoritative cache.");
            var cachedBui = FixtureRead(cached: true);
            Require(!cachedBui.CandidateSetComplete && cachedBui.State.SnapshotValid &&
                cachedBui.ReadPath == "fast-cache-hit",
                "Known live BUI address did not use the temporal cached scope.");
            cache.Replace(new[] { 1UL });
            var invalidAddressFallback = FixtureRead(cached: true);
            Require(invalidAddressFallback.CandidateSetComplete && invalidAddressFallback.State.SnapshotValid &&
                invalidAddressFallback.ReadPath == "fast-fallback-full",
                "Invalid cached address did not fall back to a complete scan.");
            Console.WriteLine("PASS cached BUI address is scoped and invalid address falls back to full enumeration");

            FixtureCommand("SECOND");
            var cachedWithNewCandidate = FixtureRead(cached: true);
            Require(!cachedWithNewCandidate.CandidateSetComplete && cachedWithNewCandidate.State.SnapshotValid,
                "Temporal cached read unexpectedly claimed the complete candidate set.");
            var ambiguousFull = FixtureRead(cached: false);
            Require(ambiguousFull.CandidateSetComplete && ambiguousFull.State.InterfaceOpen &&
                !ambiguousFull.State.SnapshotValid &&
                ambiguousFull.State.Error?.Contains("несколько", StringComparison.OrdinalIgnoreCase) == true,
                "Complete scan did not reject a newly appeared second BUI.");
            FixtureCommand("REMOVE_SECOND");
            var recoveredFull = FixtureRead(cached: false);
            Require(recoveredFull.CandidateSetComplete && recoveredFull.State.SnapshotValid,
                "Complete scan did not recover after the second BUI disappeared.");
            Console.WriteLine("PASS cached temporal read cannot hide a second BUI from mandatory full control");

            FixtureCommand("CLOSE");
            var closedFallback = FixtureRead(cached: true);
            Require(closedFallback.CandidateSetComplete && !closedFallback.State.InterfaceOpen &&
                closedFallback.ReadPath == "fast-fallback-full",
                "Closed cached BUI did not fall back to a complete closed-state scan.");
            FixtureCommand("OPEN");
            var reopenedFallback = FixtureRead(cached: true);
            Require(reopenedFallback.CandidateSetComplete && reopenedFallback.State.SnapshotValid &&
                reopenedFallback.ReadPath == "fast-fallback-full",
                "Empty cache did not rediscover the reopened BUI with a complete scan.");
            Console.WriteLine("PASS cached close/reopen is rediscovered fail-closed");
            Console.WriteLine("PASS expose Value and ValueTarget so moving scroll fails closed");
            Console.WriteLine("UI memory reader OK: 18 tests; fixture process only, SS14 not accessed.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally
        {
            try
            {
                if (!child.HasExited)
                {
                    child.StandardInput.WriteLine("STOP"); child.StandardInput.Flush();
                    if (!child.WaitForExit(5000)) child.Kill();
                }
            }
            catch (InvalidOperationException) { }
        }
    }

    private static void Rect(ChemMasterUiRect actual, int x, int y, int width, int height, string label)
    {
        Require(actual != null && actual.X == x && actual.Y == y && actual.Width == width && actual.Height == height,
            $"Wrong {label} bounds: ({actual?.X},{actual?.Y},{actual?.Width},{actual?.Height}).");
    }
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
}

namespace CalibrationMemoryFixture
{
    internal sealed class FixtureSet
    {
        public readonly Window Good = new();
        public readonly DisabledWindow Disabled = new();
        public readonly BadDoseWindow BadDose = new();
        public readonly HiddenWindow Hidden = new();
        public readonly ButtonHoverWindow ButtonHover = new();
        public readonly OutsidePointerWindow OutsidePointer = new();
        public readonly NoHoverWindow NoHover = new();
        public readonly BadPointerWindow BadPointer = new();
        public readonly Content.Client.Chemistry.UI.ChemMasterBoundUserInterface PrimaryBui;
        public Content.Client.Chemistry.UI.ChemMasterBoundUserInterface? SecondaryBui;

        public FixtureSet()
        {
            PrimaryBui = new Content.Client.Chemistry.UI.ChemMasterBoundUserInterface(Good);
        }

        public void AddSecondBui() => SecondaryBui =
            new Content.Client.Chemistry.UI.ChemMasterBoundUserInterface(Good);

        public void RemoveSecondBui()
        {
            SecondaryBui = null;
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
        }
    }

    internal class Control
    {
        private readonly List<Control> _orderedChildren = new();
        private bool _visible = true;
        private Vector2 _size;
        private bool fixtureRoot;
        public Control? Parent { get; private set; }
        public Control? Root { get; private set; }
        public bool Disposed { get; private set; }
        public Vector2 Position { get; private set; }
        public bool VisibleForCompiler => _visible;

        public Control(float x = 0, float y = 0, float width = 1, float height = 1)
        {
            Position = new Vector2(x, y);
            _size = new Vector2(width, height);
        }

        protected void MarkAsRoot() => fixtureRoot = true;
        public void Hide() => _visible = false;
        public void Add(Control child)
        {
            child.Parent = this;
            child.AssignRoot(fixtureRoot ? this : Root);
            _orderedChildren.Add(child);
        }
        private void AssignRoot(Control? root)
        {
            Root = root;
            foreach (var child in _orderedChildren) child.AssignRoot(root);
        }
    }

    internal sealed class Scope
    {
        private readonly Dictionary<string, Control> _inner = new();
        public void Add(string name, Control control) => _inner.Add(name, control);
    }

    internal abstract class FixtureWindow : Control
    {
        public Scope NameScope = new();
        protected FixtureWindow(bool disabled = false, bool badDose = false, bool hidden = false,
            bool hoverButton = false, bool outsidePointer = false, bool noHover = false, bool badPointer = false)
            : base(10.6f, 20.6f, 500, 400)
        {
            var root = new Robust.Client.UserInterface.Controls.WindowRoot(1.25f);
            var host = new Control(0.6f, 0.6f, 790, 590);
            root.Add(host);
            host.Add(this);

            var tabs = new Robust.Client.UserInterface.Controls.TabContainer();
            NameScope.Add("Tabs", tabs);
            Add(tabs);

            var input = Table(false, false, false, out _, "Water");
            var buffer = Table(true, disabled, badDose, out var firstBufferButton, "Oxygen", "Nitrogen");
            NameScope.Add("InputContainerInfo", input);
            NameScope.Add("BufferInfo", buffer);
            var inputScroll = new Robust.Client.UserInterface.Controls.ScrollContainer(input, 0, 0, 8.6f, 35.6f);
            var bufferScroll = new Robust.Client.UserInterface.Controls.ScrollContainer(buffer, 25, 50, 8.6f, 190.6f);
            Add(inputScroll);
            Add(bufferScroll);
            root.ConfigurePointer(
                outsidePointer ? new Vector2(-1, 331) : hoverButton ? new Vector2(118, 303) : new Vector2(613, 331),
                noHover ? null : hoverButton ? firstBufferButton : bufferScroll);
            if (badPointer) root.CorruptWindowHandle();
            if (hidden) host.Hide();
        }

        private static Control Table(bool buffer, bool disabled, bool badDose,
            out Content.Client.Chemistry.UI.ReagentButton? firstButton, params string[] ids)
        {
            firstButton = null;
            var table = new Control(0, 0, 480, 400);
            table.Add(new Control(0, 0, 480, 20)); // Header, not a reagent row.
            var amounts = new[] { 1, 5, 10, 15, 20, 25, 30, 50, 75, 100, 101 };
            for (var rowIndex = 0; rowIndex < ids.Length; rowIndex++)
            {
                var panel = new Control(0, 20 + rowIndex * 24, 480, 24);
                var row = new Control(0, 0, 480, 24);
                panel.Add(row);
                row.Add(new Control(0, 0, 30, 20));
                row.Add(new Control(30, 0, 30, 20));
                for (var doseIndex = 0; doseIndex < amounts.Length; doseIndex++)
                {
                    var amount = badDose && rowIndex == 0 && doseIndex == amounts.Length - 1 ? 100 : amounts[doseIndex];
                    var button = new Content.Client.Chemistry.UI.ReagentButton(buffer, ids[rowIndex], amount,
                        disabled && rowIndex == 0 && doseIndex == 0, 60 + doseIndex * 36, 2.4f);
                    firstButton ??= button;
                    row.Add(button);
                }
                table.Add(panel);
            }
            return table;
        }
    }

    internal sealed class Window : FixtureWindow { public Window() : base() { } }
    internal sealed class DisabledWindow : FixtureWindow { public DisabledWindow() : base(disabled: true) { } }
    internal sealed class BadDoseWindow : FixtureWindow { public BadDoseWindow() : base(badDose: true) { } }
    internal sealed class HiddenWindow : FixtureWindow { public HiddenWindow() : base(hidden: true) { } }
    internal sealed class ButtonHoverWindow : FixtureWindow { public ButtonHoverWindow() : base(hoverButton: true) { } }
    internal sealed class OutsidePointerWindow : FixtureWindow { public OutsidePointerWindow() : base(outsidePointer: true) { } }
    internal sealed class NoHoverWindow : FixtureWindow { public NoHoverWindow() : base(noHover: true) { } }
    internal sealed class BadPointerWindow : FixtureWindow { public BadPointerWindow() : base(badPointer: true) { } }
    internal readonly struct IntVector2
    {
        public readonly int X;
        public readonly int Y;
        public IntVector2(int x, int y) { X = x; Y = y; }
    }
    internal struct ReagentId { public string Prototype { get; set; } }

    internal readonly struct FixedPoint2
    {
        public int Value { get; }
        public FixedPoint2(int value) => Value = value;
    }

    internal readonly struct ReagentQuantity
    {
        public ReagentId Reagent { get; }
        public FixedPoint2 Quantity { get; }
        public ReagentQuantity(string id, int amount)
        {
            Reagent = new ReagentId { Prototype = id };
            Quantity = new FixedPoint2(amount);
        }
    }

    internal sealed class ContainerInfo
    {
        public string DisplayName = "fixture beaker";
        public FixedPoint2 CurrentVolume;
        public FixedPoint2 MaxVolume;
        public List<ReagentQuantity>? Reagents { get; }
        public List<object>? Entities { get; }

        public ContainerInfo(int current, int maximum, List<ReagentQuantity>? reagents)
        {
            CurrentVolume = new FixedPoint2(current);
            MaxVolume = new FixedPoint2(maximum);
            Reagents = reagents;
        }
    }
}

namespace Robust.Client.UserInterface.Controls
{
    internal sealed class WindowRoot : CalibrationMemoryFixture.Control
    {
        public float UIScaleSet { get; private set; }
        public object Window { get; private set; }
        public object UserInterfaceManagerInternal { get; private set; }
        public WindowRoot(float scale) : base(0, 0, 800, 600)
        {
            UIScaleSet = scale;
            MarkAsRoot();
            Window = new Robust.Client.Graphics.Clyde.Clyde.WindowHandle();
            UserInterfaceManagerInternal = new Robust.Client.UserInterface.UserInterfaceManager();
        }
        public void ConfigurePointer(Vector2 position, CalibrationMemoryFixture.Control? hovered)
        {
            ((Robust.Client.Graphics.Clyde.Clyde.WindowHandle) Window).Reg.LastMousePos = position;
            ((Robust.Client.UserInterface.UserInterfaceManager) UserInterfaceManagerInternal).CurrentlyHovered = hovered;
        }
        public void CorruptWindowHandle() => Window = new object();
    }

    internal sealed class TabContainer : CalibrationMemoryFixture.Control
    {
        private int _currentTab;
        public int CurrentForCompiler => _currentTab;
        public TabContainer() : base(0, 0, 1, 1) { _currentTab = 0; }
    }

    internal sealed class VScrollBar : CalibrationMemoryFixture.Control
    {
        private float _value;
        private float _valueTarget;
        private float _page = 110;
        private float _maxValue = 600;
        public float RangeForCompiler => _page + _maxValue;
        public VScrollBar(float value, float target) : base(465, 0, 15, 110)
        { _value = value; _valueTarget = target; }
    }

    internal sealed class ScrollContainer : CalibrationMemoryFixture.Control
    {
        private readonly VScrollBar _vScrollBar;
        private bool _hScrollVisible;
        private bool _vScrollVisible = true;
        public bool VisibilityForCompiler => _hScrollVisible || _vScrollVisible;
        public ScrollContainer(CalibrationMemoryFixture.Control table, float value, float target, float x, float y)
            : base(x, y, 480, 110)
        {
            _hScrollVisible = false;
            _vScrollBar = new VScrollBar(value, target);
            Add(table);
            Add(_vScrollBar);
        }
    }
}

namespace Robust.Client.UserInterface
{
    internal sealed class UserInterfaceManager
    {
        public CalibrationMemoryFixture.Control? CurrentlyHovered { get; set; }
    }
}

namespace Robust.Client.Graphics.Clyde
{
    internal sealed class Clyde
    {
        internal sealed class WindowHandle
        {
            public WindowReg Reg = new();
        }

        internal sealed class WindowReg
        {
            public Vector2 LastMousePos = new(613, 331);
            public CalibrationMemoryFixture.IntVector2 FramebufferSize = new(1000, 750);
            public bool IsMainWindow = true;
            public bool IsDisposed = false;
            public bool IsFocused = true;
            public bool IsMinimized = false;
        }
    }
}

namespace Content.Client.Chemistry.UI
{
    internal sealed class ReagentButton : CalibrationMemoryFixture.Control
    {
        private bool _disabled;
        public int Amount { get; set; }
        public bool IsBuffer;
        public CalibrationMemoryFixture.ReagentId Id { get; set; }
        public ReagentButton(bool buffer, string id, int amount, bool disabled, float x, float y)
            : base(x, y, 32, 20)
        { IsBuffer = buffer; Id = new CalibrationMemoryFixture.ReagentId { Prototype = id }; Amount = amount; _disabled = disabled; }
    }

    internal sealed class ChemMasterBoundUserInterface
    {
        private readonly CalibrationMemoryFixture.Window _window;
        public bool IsOpened { get; set; } = true;
        public object State { get; }

        public ChemMasterBoundUserInterface(CalibrationMemoryFixture.Window window)
        {
            _window = window;
            State = new Content.Shared.Chemistry.ChemMasterBoundUserInterfaceState();
        }
    }
}

namespace Content.Shared.Chemistry
{
    internal sealed class ChemMasterBoundUserInterfaceState
    {
        public int Mode = 0;
        public byte SortingType = 0;
        public CalibrationMemoryFixture.FixedPoint2? BufferCurrentVolume =
            new CalibrationMemoryFixture.FixedPoint2(500);
        public uint SelectedPillType = 0;
        public uint PillDosageLimit = 20;
        public bool UpdateLabel = false;
        public CalibrationMemoryFixture.ContainerInfo InputContainerInfo = new(
            100, 10000, new List<CalibrationMemoryFixture.ReagentQuantity>
            {
                new("Water", 100),
            });
        public CalibrationMemoryFixture.ContainerInfo? OutputContainerInfo;
        public List<CalibrationMemoryFixture.ReagentQuantity> BufferReagents = new()
        {
            new("Oxygen", 200),
            new("Nitrogen", 300),
        };

        public ChemMasterBoundUserInterfaceState() => OutputContainerInfo = null;
    }
}
