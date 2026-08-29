using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using Ss14.Chemistry;

internal static class ChemCalibrationTests
{
    private static int passed;
    public static int Main()
    {
        try
        {
            Test("complete input profile", delegate { Equal(0, ChemCalibration.Validate(Profile()).Count); });
            Test("partial profile remains draft", delegate { var p = Profile(); p.Points.Remove("input.5"); True(ChemCalibration.Validate(p).Count > 0); });
            Test("scroll origin confirmation required", delegate { var p = Profile(); p.ScrollTopConfirmed = false; True(ChemCalibration.Validate(p).Count > 0); });
            Test("no guessed column interpolation", delegate { var p = Profile(); Equal(628, (int)Preview(p, Ui(), "Nitrogen", "5", 0, p.Regions["frame"]).X); });
            Test("actual UI order overrides raw order", delegate { var p = Profile(); Equal(584, (int)Preview(p, Ui(), "Nitrogen", "1", 0, p.Regions["frame"]).Y); });
            Test("raw ID order need not equal display order", delegate { ChemCalibration.ValidateRows(Ui().BufferRows, new[] { "Nitrogen", "Water", "Oxygen" }); });
            Test("moved frame translates coordinates", delegate { var p = Profile(); var point = Preview(p, Ui(), "Oxygen", "1", 0, new CalibrationRect { X = 140, Y = 230, Width = 860, Height = 830 }); Equal(700, (int)point.X); Equal(760, (int)point.Y); });
            Test("resized frame rejected", delegate { var p = Profile(); Throws(delegate { Preview(p, Ui(), "Nitrogen", "1", 0, new CalibrationRect { Width = 861, Height = 830 }); }); });
            Test("explicit scroll offset maps visible row", delegate { var p = Profile(); Equal(560, (int)Preview(p, Ui(), "Nitrogen", "1", 1, p.Regions["frame"]).Y); });
            Test("row above viewport rejected", delegate { var p = Profile(); Throws(delegate { Preview(p, Ui(), "Oxygen", "1", 1, p.Regions["frame"]); }); });
            Test("row below viewport rejected", delegate { var p = Profile(); p.Regions["bufferViewport"].Height = 58; Throws(delegate { Preview(p, Ui(), "Water", "1", 0, p.Regions["frame"]); }); });
            Test("partially clipped row rejected", delegate { var p = Profile(); p.Regions["bufferViewport"].Y = 551; p.Regions["bufferViewport"].Height = 250; Throws(delegate { Preview(p, Ui(), "Oxygen", "1", 0, p.Regions["frame"]); }); });
            Test("missing live UI data blocks mapping", delegate { var p = Profile(); Throws(delegate { Preview(p, null, "Oxygen", "1", 0, p.Regions["frame"]); }); });
            Test("invalid UI order blocks mapping", delegate { var p = Profile(); var ui = Ui(); ui.RowOrderValid = false; Throws(delegate { Preview(p, ui, "Oxygen", "1", 0, p.Regions["frame"]); }); });
            Test("moving live scroll blocks mapping", delegate { var p = Profile(); var ui = Ui(); ui.BufferScroll.Stable = false; Throws(delegate { Preview(p, ui, "Oxygen", "1", 0, p.Regions["frame"]); }); });
            Test("untrusted order source rejected", delegate { var p = Profile(); var ui = Ui(); ui.Source = "raw-inventory"; Throws(delegate { Preview(p, ui, "Oxygen", "1", 0, p.Regions["frame"]); }); });
            Test("different inventory rows rejected", delegate { Throws(delegate { ChemCalibration.ValidateRows(Ui().BufferRows, new[] { "Nitrogen", "Water", "Iron" }); }); });
            Test("duplicate raw IDs rejected", delegate { Throws(delegate { ChemCalibration.ValidateRows(Ui().BufferRows, new[] { "Water", "Water", "Oxygen" }); }); });
            Test("duplicate UI IDs rejected", delegate { var rows = Ui().BufferRows; rows[1].Prototype = "Oxygen"; Throws(delegate { ChemCalibration.ValidateRows(rows, new[] { "Nitrogen", "Water", "Oxygen" }); }); });
            Test("non-contiguous row indices rejected", delegate { var rows = Ui().BufferRows; rows[1].RowIndex = 5; Throws(delegate { ChemCalibration.ValidateRows(rows, new[] { "Nitrogen", "Water", "Oxygen" }); }); });
            Test("empty inventories supported", delegate { ChemCalibration.ValidateRows(new List<ChemMasterUiRow>(), new string[0]); });
            Test("missing reagent rejected", delegate { var p = Profile(); Throws(delegate { Preview(p, Ui(), "Unknown", "1", 0, p.Regions["frame"]); }); });
            Test("unknown dose rejected", delegate { var p = Profile(); Throws(delegate { Preview(p, Ui(), "Water", "3", 0, p.Regions["frame"]); }); });
            Test("unknown scroll position rejected", delegate { var p = Profile(); Throws(delegate { Preview(p, Ui(), "Water", "1", -1, p.Regions["frame"]); }); });
            Test("off-image frame rejected", delegate { var p = Profile(); p.Regions["frame"].Width = 5000; True(ChemCalibration.Validate(p).Count > 0); });
            Test("point outside panel rejected", delegate { var p = Profile(); p.Points["bufferSort"].X = 999; True(ChemCalibration.Validate(p).Count > 0); });
            Test("reversed dose columns rejected", delegate { var p = Profile(); p.Points["buffer.5"].X = 100; True(ChemCalibration.Validate(p).Count > 0); });
            Test("different row for sample doses rejected", delegate { var p = Profile(); p.Points["buffer.10"].Y += 24; True(ChemCalibration.Validate(p).Count > 0); });
            Test("invalid row pitch rejected", delegate { var p = Profile(); p.Points["buffer.next"].Y = 560; True(ChemCalibration.Validate(p).Count > 0); });
            Test("non-finite geometry rejected", delegate { var p = Profile(); p.Points["input.1"].X = Double.NaN; True(ChemCalibration.Validate(p).Count > 0); });
            Test("null point rejected", delegate { var p = Profile(); p.Points["bufferSort"] = null; True(ChemCalibration.Validate(p).Count > 0); });
            Test("unknown schema rejected", delegate { var p = Profile(); p.SchemaVersion = 9; True(ChemCalibration.Validate(p).Count > 0); });
            Test("JSON preserves source pixel coordinates", delegate
            {
                var p = Profile(); p.Points["bufferSort"].X += 0.25;
                var serializer = new DataContractJsonSerializer(typeof(CalibrationProfile), new DataContractJsonSerializerSettings { UseSimpleDictionaryFormat = true });
                using (var stream = new MemoryStream())
                {
                    serializer.WriteObject(stream, p); stream.Position = 0;
                    var restored = (CalibrationProfile)serializer.ReadObject(stream);
                    True(restored.Points["bufferSort"].X == p.Points["bufferSort"].X);
                    Equal(0, ChemCalibration.Validate(restored).Count);
                }
            });
            Test("output profile has no repeated reagent grid", delegate { True(!ChemCalibration.Steps("output").Any(x => x.Id == "buffer.1")); });
            Console.WriteLine("Calibration OK: " + passed + " tests; no game process or input used.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }

    private static CalibrationProfile Profile()
    {
        var p = new CalibrationProfile { ImageWidth = 1000, ImageHeight = 900, ScrollTopConfirmed = true };
        p.Regions["frame"] = new CalibrationRect { X = 40, Y = 30, Width = 860, Height = 830 };
        p.Points["tabInput"] = new CalibrationPoint(85, 70); p.Points["tabOutput"] = new CalibrationPoint(155, 70);
        p.Points["inputEject"] = new CalibrationPoint(835, 115); p.Points["bufferSort"] = new CalibrationPoint(580, 480);
        p.Points["bufferTransfer"] = new CalibrationPoint(720, 480);
        int[] xs = { 600, 628, 654, 680, 706, 732, 758, 784, 810, 836, 864 };
        foreach (string list in new[] { "input", "buffer" })
        {
            int top = list == "input" ? 150 : 540;
            p.Regions[list + "Viewport"] = new CalibrationRect { X = 60, Y = top, Width = 820, Height = 280 };
            for (int i = 0; i < xs.Length; i++) p.Points[list + "." + ChemCalibration.Doses[i]] = new CalibrationPoint(xs[i], top + 20);
            p.Points[list + ".next"] = new CalibrationPoint(xs[0], top + 44);
        }
        return p;
    }
    private static ChemMasterUiSnapshot Ui()
    {
        return new ChemMasterUiSnapshot { RowOrderValid = true,
            InputScroll = new ChemMasterScrollState { Stable = true }, BufferScroll = new ChemMasterScrollState { Stable = true }, BufferRows = new List<ChemMasterUiRow> {
            new ChemMasterUiRow(0, "Oxygen"), new ChemMasterUiRow(1, "Nitrogen"), new ChemMasterUiRow(2, "Water") } };
    }
    private static CalibrationPoint Preview(CalibrationProfile p, ChemMasterUiSnapshot ui, string prototype, string dose, int scroll, CalibrationRect frame)
    { return ChemCalibration.PreviewReagentPoint(p, ui, "buffer", prototype, dose, scroll, frame); }
    private static void Test(string name, Action test) { test(); passed++; Console.WriteLine("PASS " + name); }
    private static void Equal(int expected, int actual) { if (expected != actual) throw new Exception("Expected " + expected + ", got " + actual); }
    private static void True(bool value) { if (!value) throw new Exception("Assertion failed"); }
    private static void Throws(Action action) { try { action(); } catch (InvalidOperationException) { return; } catch (ArgumentException) { return; } throw new Exception("Expected rejection"); }
}
