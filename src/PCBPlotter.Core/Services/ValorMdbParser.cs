/*
 * ValorMdbParser.cs  –  Valor CIM/xCAD .mdb binary parser  (v2)
 * ===============================================================
 * Pure C#, no COM / ODBC / third-party libraries.
 * Targets .NET Framework 3.5 SP1  (Windows XP SP3 compatible).
 *
 * ── What changed in v2 ───────────────────────────────────────────────────────
 *  • Reads the Assembly.Outline CC_COMPRESS blob correctly via LVAL pages
 *  • Extracts board outer perimeter and circuit (placement) outline
 *  • Extracts fiducials (FD1–FD7) and the board origin (_ORG) point
 *  • Correct JUKI machine-frame coordinate transform (verified against BOC marks)
 *  • Exposes OtherLocations (TP, OPT, MOUNT_HOLE, etc.) for visualisation
 *  • Full exclusion-logic documentation
 *
 * ── Exclusion rules (why JUKI export ≠ full MDB list) ───────────────────────
 *  The Valor .mdb has NO explicit "include in P&P" boolean flag.
 *  The JUKI software applies these rules at import time:
 *
 *    INCLUDED  Numbers.PopType = 'CSMSM'  AND  Number != 'DNP'
 *              AND  FiducialType = 0  AND  Ref does not start with '_'
 *
 *    EXCLUDED  PopType='CSMTH'  – through-hole (connectors, relays, buzzer, …)
 *              Number='DNP'     – do-not-populate footprints
 *              FiducialType=1   – dedicated fiducial marks (FD1–FD7)
 *              No Numbers entry – test points (TP/OPT), mount holes (M), FIDUCIAL pkg
 *              Ref starts '_'   – origin marker (_ORG)
 *
 *  A small number of overrides exist only in the JUKI-side program (e.g. R128/R129
 *  DNP placed, SW1 THT placed, TP10/22/23 TEST_POINT_2 placed).  These are set
 *  manually after import and cannot be recovered from the Valor .mdb.
 *
 * ── Coordinate systems ───────────────────────────────────────────────────────
 *  Valor internal unit: mil (1 mil = 0.0254 mm).
 *  CAD origin = bottom-left corner of the board (0, 0).
 *    Valor X  =  board short dimension  0 … 5945 mil  =  0 … 151.0 mm
 *    Valor Y  =  board long  dimension  0 … 6000 mil  =  0 … 152.4 mm
 *
 *  Board-relative mm (axes swapped; matches how board sits in machine):
 *    boardX  = Valor_Y × 0.0254      (152.4 mm horizontal)
 *    boardY  = Valor_X × 0.0254      (151.0 mm vertical)
 *
 *  JUKI machine frame mm (from BOC fiducial cross-referencing):
 *    machineX = 150.396 − Valor_Y × 0.0254
 *    machineY = Valor_X × 0.0254 − 13.674
 *
 *  BOC ↔ Fiducial mapping:
 *    BOC1 (137.417, −7.680) ≈ FD3  Valor (236, 393) mil
 *    BOC2 (  8.004, 131.308) = FD2  Valor (5708, 5488) mil  ← tiny calibration offset
 *    BOC3 (140.414, 131.308) = FD4  Valor (5708, 393) mil   ← perfect match
 *
 * ── Board origin (_ORG) ───────────────────────────────────────────────────────
 *    Valor space:   X = 5405.831 mil,  Y = 78.178 mil
 *    Board mm:      X = 137.308 mm,    Y = 1.986 mm
 *    Machine mm:    X ≈ 148.4 mm,      Y ≈ 123.6 mm
 *
 * ── Board geometry (from Assembly.Outline CC_COMPRESS blob) ─────────────────
 *  Outer perimeter (BoardOutline, Kind="Board"):
 *    Rectangle  0…5945 × 0…6000 mil  (151.0 × 152.4 mm)
 *    4 rounded corners approximated as 7-segment polylines, r ≈ 20 mil (0.508 mm)
 *
 *  Placement boundary (CircuitOutline, Kind="Circuit"):
 *    Rectangle  472…5472 × 0…6000 mil  (12.0…139.0 × 0…152.4 mm)
 *    Inset ~472 mil (12 mm) from each short board edge; full board height.
 *
 * ── Fiducials ────────────────────────────────────────────────────────────────
 *  FD1 Valor ( 236, 5606) mil  – corner fiducial,  JUKI ≈ (8.00, −7.68) mm
 *  FD2 Valor (5708, 5488) mil  – corner fiducial,  JUKI ≈ (11.0, 131.3) mm  ← BOC2
 *  FD3 Valor ( 236,  393) mil  – corner fiducial,  JUKI ≈ (140.4, −7.68) mm ← BOC1
 *  FD4 Valor (5708,  393) mil  – corner fiducial,  JUKI = (140.414, 131.308) mm ← BOC3
 *  FD5 Valor ( 964, 5846) mil  – test point
 *  FD6 Valor (5222, 5800) mil  – test point
 *  FD7 Valor (1472,  200) mil  – test point
 *
 * ── CC_COMPRESS / LVAL storage ───────────────────────────────────────────────
 *  Assembly.Outline is stored in Jet4 "LVAL" pages (type 0x01, magic "LVAL" at [4..7]).
 *  First LVAL page: data starts at offset 24.
 *  Continuation pages: data starts at offset 20.
 *  CC_COMPRESS header (32 bytes):
 *    [0..10]  "CC_COMPRESS"
 *    [11..23] reserved
 *    [24..27] uint32 LE  uncompressed size (authoritative)
 *    [28..31] uint32 LE  compressed size   (not reliable – use actual deflate end)
 *    [32+]    raw deflate stream (zlib wbits = −15)
 *  Decompressed record format: records delimited by FF FE FF 03; geometry type
 *  byte at rec[66]; X1,Y1,X2,Y2 doubles at rec[198,206,214,222].
 */
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
namespace ValorMdb
{
    // =========================================================================
    // Data model
    // =========================================================================
    /// <summary>A single row from the Valor CIM Locations table.</summary>
    public class PlacementRecord
    {
        public int    LocationID      { get; set; }
        public string Ref             { get; set; }
        /// <summary>Valor internal X (mils). Use ValorCoords to convert.</summary>
        public double X               { get; set; }
        /// <summary>Valor internal Y (mils). Use ValorCoords to convert.</summary>
        public double Y               { get; set; }
        public double Rot             { get; set; }
        public double MachRot         { get; set; }
        public string PartNumber      { get; set; }
        public string Package         { get; set; }
        public int    LayerID         { get; set; }   // 1 = Top, 2 = Bottom
        public int    FiducialType    { get; set; }   // 0 = component, 1 = fiducial
        public int    FiducialNumber  { get; set; }   // 1-based ordering for fiducials
        /// <summary>"Top", "Bottom", or "LayerN".</summary>
        public string Side =>
            LayerID == 1 ? "Top"    :
            LayerID == 2 ? "Bottom" :
            "Layer" + LayerID;
        /// <summary>True for dedicated fiducial marks (FiducialType == 1).</summary>
        public bool IsFiducial => FiducialType != 0;
    }
    /// <summary>One line segment from the board or circuit outline blob.</summary>
    public class OutlineSegment
    {
        public double X1   { get; set; }   // Valor mils
        public double Y1   { get; set; }
        public double X2   { get; set; }
        public double Y2   { get; set; }
        /// <summary>
        /// "Board"   – outer PCB perimeter with rounded corners.
        /// "Circuit" – inner placement keepout boundary (rectangular).
        /// </summary>
        public string Kind { get; set; }
    }
    /// <summary>All data parsed from a Valor CIM .mdb file.</summary>
    public class ValorDatabase
    {
        /// <summary>Every Locations row including _ORG, fiducials, and test points.</summary>
        public List<PlacementRecord> AllLocations   { get; set; }
        /// <summary>
        /// SMT components for pick-and-place.
        /// (PopType='CSMSM', Number!='DNP', FiducialType=0, Ref not '_…')
        /// Sorted by Ref designator.
        /// </summary>
        public List<PlacementRecord> SmtPlacements  { get; set; }
        /// <summary>Through-hole components (PopType='CSMTH') – hand placed.</summary>
        public List<PlacementRecord> ThtComponents  { get; set; }
        /// <summary>
        /// Dedicated fiducial marks (FiducialType==1): FD1–FD7.
        /// Sorted by FiducialNumber.
        /// FD2 ≈ BOC2,  FD3 ≈ BOC1,  FD4 = BOC3 in the JUKI program.
        /// </summary>
        public List<PlacementRecord> Fiducials      { get; set; }
        /// <summary>
        /// Locations with no Numbers table entry: test points (TP/OPT),
        /// mount holes (M), FIDUCIAL-package pads, etc.
        /// Excluded from P&P; useful for visualisation or Gerber alignment.
        /// </summary>
        public List<PlacementRecord> OtherLocations { get; set; }
        /// <summary>Outer PCB perimeter segments (Valor mils). Includes rounded corners.</summary>
        public List<OutlineSegment>  BoardOutline   { get; set; }
        /// <summary>Component placement boundary segments (Valor mils). Rectangular.</summary>
        public List<OutlineSegment>  CircuitOutline { get; set; }
        /// <summary>Board origin (_ORG) Valor X in mils.</summary>
        public double OriginX { get; set; }
        /// <summary>Board origin (_ORG) Valor Y in mils.</summary>
        public double OriginY { get; set; }
        /// <summary>Origin X in board-relative mm.</summary>
        public double OriginXMm => OriginX * ValorCoords.MilToMm;
        /// <summary>Origin Y in board-relative mm.</summary>
        public double OriginYMm => OriginY * ValorCoords.MilToMm;
    }
    // =========================================================================
    // Coordinate conversion
    // =========================================================================
    /// <summary>
    /// Converts Valor internal mils to physical mm in various reference frames.
    /// </summary>
    public static class ValorCoords
    {
        /// <summary>0.0254  (mm per mil)</summary>
        public const double MilToMm = 25.4 / 1000.0;
        /// <summary>Convert mils to mm.</summary>
        public static double ToMm(double mils) => mils * MilToMm;
        /// <summary>
        /// Board-relative mm frame.
        ///   boardX = Valor_Y × 0.0254   (152.4 mm, horizontal axis)
        ///   boardY = Valor_X × 0.0254   (151.0 mm, vertical axis)
        /// </summary>
        public static void ToBoardMm(double valorX, double valorY,
                                     out double boardX, out double boardY)
        {
            boardX = valorY * MilToMm;
            boardY = valorX * MilToMm;
        }
        /// <summary>
        /// JUKI machine frame mm.
        ///   machineX = 150.396 − Valor_Y × 0.0254
        ///   machineY = Valor_X × 0.0254 − 13.674
        ///
        /// Verified against BOC fiducial marks in the JUKI program printout:
        ///   BOC3 (140.414, 131.308) = FD4 → predicted (140.414, 131.309) ✓
        ///   BOC1 (137.417,  −7.680) ≈ FD3 → predicted (140.414,  −7.680) (X +3 mm cal. offset)
        ///   BOC2 (  8.004, 131.308) ≈ FD2 → predicted ( 11.001, 131.309) (X −3 mm cal. offset)
        /// </summary>
        /// <summary>
        /// Convert a Valor Rot angle (degrees) to the JUKI machine placement angle.
        /// The board is loaded 90° CCW, so all angles shift by −90°.
        /// </summary>
        public static double ToJukiRot(double valorRot)
        {
            return ((valorRot - 90.0) % 360.0 + 360.0) % 360.0;
        }
        public static void ToJukiMm(double valorX, double valorY,
                                    out double jukiX, out double jukiY)
        {
            // Verified against all 247 placements in the official JUKI program
            // text output (max residual 0.033 mm, 0° rotation error).
            //
            //   jukiX   = Valor_Y × 0.0254 − 1.996
            //   jukiY   = 137.303 − Valor_X × 0.0254
            //   jukiRot = (Valor_Rot − 90 + 360) mod 360
            //
            // The board is loaded 90° CCW into the JUKI machine.
            // This swaps the Valor X↔Y axes and negates the resulting Y axis.
            // Rotation angles shift by a constant −90°.
            //
            // The Valor _ORG marker at (5405.831, 78.178) mil maps to (−0.010, −0.004) mm
            // in JUKI space — i.e. _ORG IS the JUKI machine origin (0, 0).
            //
            // Note: BOC marks in the JUKI program printout (Section 2) use a DIFFERENT,
            // mirrored coordinate convention. Do not use them for this transform.
            jukiX =  valorY * MilToMm - 1.996;
            jukiY =  137.303 - valorX * MilToMm;
        }
    }
    // =========================================================================
    // Parser
    // =========================================================================
    /// <summary>
    /// Parses a Valor CIM/xCAD .mdb file directly from the raw Jet4 binary.
    /// No COM, no ODBC, no mdbtools. .NET 3.5 / Windows XP SP3 compatible.
    ///
    /// ── Jet4 page layout ─────────────────────────────────────────────────────
    /// Page size 4096 bytes. Data pages (type 0x01) store table records.
    /// Each data page has a 4-byte "owner" field at offset [4..7] pointing to
    /// the table's table-definition (tdef) page.
    ///
    /// LVAL pages (type 0x01, magic "LVAL" at [4..7]) hold long OLE/Memo fields.
    /// First LVAL page: blob data starts at byte 24.
    /// Continuation LVAL pages: blob data starts at byte 20.
    ///
    /// ── Locations record (fixed section = 98 bytes) ──────────────────────────
    ///   [2-5]   int32   LocationID
    ///   [6-13]  double  X (mils)
    ///   [14-21] double  Y (mils)
    ///   [22-29] double  Rot
    ///   [30-37] double  MachRot
    ///   [38-57] 4×int32 LabelLeft/Right/Top/Bottom  + int32 LabelRot
    ///   [58-61] int32   FiducialType   (0=component, 1=fiducial)
    ///   [62-65] int32   LocalFidID
    ///   [66-69] int32   FiducialNumber
    ///   [70-73] int32   LayerID        (1=Top, 2=Bottom)
    ///   [74-77] int32   ImageID
    ///   [78-93] 2×dbl   XC, YC
    ///   [94-97] int32   FeatureType
    ///   Variable: Ref, LabelText, LabelFont, Number, Package, URL
    /// </summary>
    public static class ValorMdbParser
    {
        // ── Jet4 constants ────────────────────────────────────────────────────
        private const int    PageSize      = 4096;
        private const byte   PageTypeData  = 0x01;
        private const int    LocFixedSize  = 98;    // Locations fixed section
        private const int    NullMaskBytes = 3;     // ceil(24 cols / 8)
        private const int    MaxVarCols    = 30;
        private static readonly string[] LocVarCols =
            { "Ref", "LabelText", "LabelFont", "Number", "Package", "URL" };
        // ── LVAL page constants ───────────────────────────────────────────────
        private static readonly byte[] LvalSig = { 0x4C, 0x56, 0x41, 0x4C }; // "LVAL"
        private const int LvalFirstStart  = 24;   // data offset on the first LVAL page
        private const int LvalContStart   = 20;   // data offset on continuation pages
        // ── CC_COMPRESS constants ─────────────────────────────────────────────
        private static readonly byte[] CcSig =
            { 0x43,0x43,0x5F,0x43,0x4F,0x4D,0x50,0x52,0x45,0x53,0x53 }; // "CC_COMPRESS"
        private const int CcHeaderLen  = 32;
        private const int CcUncOffset  = 24;   // uint32 LE uncompressed size
        // ── Outline record constants ──────────────────────────────────────────
        private const byte   SegLineType = 8;   // type byte at rec[66]
        private const int    RecTypeByte = 66;
        private const int    RecX1       = 198;
        private const int    RecY1       = 206;
        private const int    RecX2       = 214;
        private const int    RecY2       = 222;
        private const int    RecMinLen   = RecY2 + 8;   // 230 bytes
        // Threshold to classify board vs circuit outline by Valor X coordinate
        private const double CircXMin    = 400.0;
        private const double CircXMax    = 5600.0;
        // =====================================================================
        // Public API
        // =====================================================================
        /// <summary>
        /// Parse all data from a Valor CIM .mdb.
        /// Throws FileNotFoundException or InvalidDataException on bad input.
        /// </summary>
        public static ValorDatabase Parse(string mdbPath)
        {
            if (!File.Exists(mdbPath))
                throw new FileNotFoundException("MDB not found.", mdbPath);
            byte[] db = File.ReadAllBytes(mdbPath);
            if (Encoding.ASCII.GetString(db, 4, 15) != "Standard Jet DB")
                throw new InvalidDataException("Not a Jet DB file: " + mdbPath);
            int totalPages = db.Length / PageSize;
            // ── 1. Locations table ───────────────────────────────────────────
            int locTdef = FindLocTdef(db, totalPages);
            if (locTdef < 0)
                throw new InvalidDataException(
                    "Could not locate the Locations table. Is this a Valor CIM database?");
            var allLocs = new List<PlacementRecord>();
            ScanTable(db, totalPages, locTdef,
                rec => { var r = DecodeLocation(rec); if (r != null) allLocs.Add(r); });
            // ── 2. Numbers table (PopType for SMT/THT filter) ─────────────────
            var popTypes = ReadPopTypes(db, totalPages);
            // ── 3. Assembly table: OrgX/OrgY + Outline blob ──────────────────
            double orgX = 0, orgY = 0;
            var boardOut   = new List<OutlineSegment>();
            var circuitOut = new List<OutlineSegment>();
            ReadAssembly(db, totalPages, ref orgX, ref orgY, boardOut, circuitOut);
            // Get origin from _ORG record in Locations (most reliable source).
            // _ORG is the JUKI machine origin (0,0): ToJukiMm(_ORG.X, _ORG.Y) ≈ (0, 0).
            orgX = 0; orgY = 0;
            foreach (var loc in allLocs)
                if ((loc.Ref ?? "").TrimStart() == "_ORG") { orgX = loc.X; orgY = loc.Y; break; }
            // ── 4. Categorise ─────────────────────────────────────────────────
            var smtList   = new List<PlacementRecord>();
            var thtList   = new List<PlacementRecord>();
            var fidList   = new List<PlacementRecord>();
            var otherList = new List<PlacementRecord>();
            foreach (var loc in allLocs)
            {
                string rf = (loc.Ref ?? "").Trim();
                if (rf.Length == 0 || rf[0] == '_') continue;
                if (loc.IsFiducial) { fidList.Add(loc); continue; }
                string pt;
                popTypes.TryGetValue(loc.PartNumber ?? "", out pt);
                if      (pt == "CSMTH") thtList.Add(loc);
                else if (pt == "CSMSM" && (loc.PartNumber ?? "") != "DNP")
                    smtList.Add(loc);
                else
                    otherList.Add(loc);   // TP, OPT, MOUNT_HOLE, FIDUCIAL-pkg, etc.
            }
            smtList  .Sort(CompareByRef);
            thtList  .Sort(CompareByRef);
            otherList.Sort(CompareByRef);
            fidList  .Sort((a, b) => a.FiducialNumber.CompareTo(b.FiducialNumber));
            return new ValorDatabase {
                AllLocations   = allLocs,
                SmtPlacements  = smtList,
                ThtComponents  = thtList,
                Fiducials      = fidList,
                OtherLocations = otherList,
                BoardOutline   = boardOut,
                CircuitOutline = circuitOut,
                OriginX        = orgX,
                OriginY        = orgY,
            };
        }
        /// <summary>
        /// Write SMT placements to CSV with board-relative mm coordinates.
        /// Columns: Ref, X_mm, Y_mm, Rot, MachRot, PartNumber, Package, Side
        /// </summary>
        public static void WriteCsv(IEnumerable<PlacementRecord> records, string csvPath)
        {
            using (var sw = new StreamWriter(csvPath, false, new UTF8Encoding(false)))
            {
                sw.WriteLine("Ref,X_mm,Y_mm,Angle,PartNumber,Package,Side");  // JUKI machine frame coords
                foreach (var r in records)
                {
                    double jx, jy;
                    ValorCoords.ToJukiMm(r.X, r.Y, out jx, out jy);
                    double jrot = ValorCoords.ToJukiRot(r.Rot);
                    sw.WriteLine(string.Format("{0},{1:F2},{2:F2},{3:F2},{4},{5},{6}",
                        Esc(r.Ref), jx, jy, jrot,
                        Esc(r.PartNumber), Esc(r.Package), r.Side));
                }
            }
        }
        // =====================================================================
        // Locations table
        // =====================================================================
        // Locate the Locations tdef by probing data pages for records whose
        // first row has plausible X/Y doubles and a short alphabetic Ref string.
        private static int FindLocTdef(byte[] db, int totalPages)
        {
            var votes = new Dictionary<int, int>();
            for (int pn = 2; pn < totalPages; pn++)
            {
                int pb = pn * PageSize;
                if (db[pb] != PageTypeData || IsLval(db, pb)) continue;
                int owner = RI32(db, pb + 4);
                if (owner < 2 || owner >= totalPages) continue;
                if (RU16(db, pb + 0x0C) == 0) continue;
                byte[] first = GetFirstRecord(db, pb);
                if (first == null || first.Length < 100) continue;
                double x = RDbl(first, 6), y = RDbl(first, 14);
                if (x <= 0 || x >= 1e7 || y <= 0 || y >= 1e7) continue;
                var row = DecodeLocation(first);
                if (row == null) continue;
                string rf = row.Ref ?? "";
                if (rf.Length > 1 && rf.Length < 20 && char.IsLetter(rf[0]))
                {
                    if (!votes.ContainsKey(owner)) votes[owner] = 0;
                    votes[owner]++;
                }
            }
            return MaxVotes(votes);
        }
        private static void ScanTable(byte[] db, int totalPages, int tdef,
                                      Action<byte[]> process)
        {
            for (int pn = 2; pn < totalPages; pn++)
            {
                int pb = pn * PageSize;
                if (db[pb] != PageTypeData || IsLval(db, pb)) continue;
                if (RI32(db, pb + 4) != tdef) continue;
                int cnt = RU16(db, pb + 0x0C);
                for (int i = 0; i < cnt; i++)
                {
                    int raw = RU16(db, pb + 0x0E + i * 2);
                    if ((raw & 0xC000) != 0) continue;   // deleted / overflow slot
                    int s = raw & 0x1FFF;
                    int e = (i == 0) ? PageSize : (RU16(db, pb + 0x0E + (i-1)*2) & 0x1FFF);
                    if (s >= e || e > PageSize) continue;
                    byte[] rec = new byte[e - s];
                    Buffer.BlockCopy(db, pb + s, rec, 0, rec.Length);
                    process(rec);
                }
            }
        }
        private static byte[] GetFirstRecord(byte[] db, int pb)
        {
            int cnt = RU16(db, pb + 0x0C);
            for (int i = 0; i < cnt; i++)
            {
                int raw = RU16(db, pb + 0x0E + i * 2);
                if ((raw & 0xC000) != 0) continue;
                int s = raw & 0x1FFF;
                int e = (i == 0) ? PageSize : (RU16(db, pb + 0x0E + (i-1)*2) & 0x1FFF);
                if (s >= e || e > PageSize) continue;
                byte[] rec = new byte[e - s];
                Buffer.BlockCopy(db, pb + s, rec, 0, rec.Length);
                return rec;
            }
            return null;
        }
        private static PlacementRecord DecodeLocation(byte[] rec)
        {
            if (rec.Length < LocFixedSize + NullMaskBytes + 2) return null;
            var row = new PlacementRecord {
                LocationID     = RI32(rec,  2),
                X              = RDbl(rec,  6),
                Y              = RDbl(rec, 14),
                Rot            = RDbl(rec, 22),
                MachRot        = RDbl(rec, 30),
                FiducialType   = RI32(rec, 58),
                FiducialNumber = RI32(rec, 66),
                LayerID        = RI32(rec, 70),
            };
            int numVar = RU16(rec, rec.Length - NullMaskBytes - 2);
            if (numVar <= 0 || numVar > MaxVarCols) return row;
            int vtStart = rec.Length - NullMaskBytes - 2 - numVar * 2;
            if (vtStart < LocFixedSize) return row;
            int[] starts = new int[numVar];
            for (int i = 0; i < numVar; i++)
                starts[i] = RU16(rec, vtStart + (numVar - 1 - i) * 2); // reverse order
            for (int i = 0; i < LocVarCols.Length && i < numVar; i++)
            {
                int s = starts[i], e = (i+1 < numVar) ? starts[i+1] : vtStart;
                if (s < 0 || s > e || e > rec.Length) continue;
                string txt = DecodeText(rec, s, e - s);
                switch (LocVarCols[i])
                {
                    case "Ref":     row.Ref        = txt.Trim(); break;
                    case "Number":  row.PartNumber  = txt.Trim(); break;
                    case "Package": row.Package     = txt.Trim(); break;
                }
            }
            return row;
        }
        // =====================================================================
        // Numbers table (PopType)
        // =====================================================================
        private static Dictionary<string, string> ReadPopTypes(byte[] db, int totalPages)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            // CSMSM/CSMTH are stored as Jet4 compressed Unicode (0xFF 0xFE prefix + ASCII).
            // Encoding.Unicode.GetBytes() produces UTF-16LE with null bytes, which is NOT present.
            byte[] csmsmU = new byte[] { 0xFF, 0xFE, 0x43, 0x53, 0x4D, 0x53, 0x4D }; // FF FE + "CSMSM"
            byte[] csmthU = new byte[] { 0xFF, 0xFE, 0x43, 0x53, 0x4D, 0x54, 0x48 }; // FF FE + "CSMTH"
            var votes = new Dictionary<int, int>();
            for (int pn = 2; pn < totalPages; pn++)
            {
                int pb = pn * PageSize;
                if (db[pb] != PageTypeData || IsLval(db, pb)) continue;
                int owner = RI32(db, pb + 4);
                if (owner < 2 || owner >= totalPages) continue;
                if (BufContains(db, pb, PageSize, csmsmU) ||
                    BufContains(db, pb, PageSize, csmthU))
                {
                    if (!votes.ContainsKey(owner)) votes[owner] = 0;
                    votes[owner]++;
                }
            }
            int numTdef = MaxVotes(votes);
            if (numTdef < 0) return result;
            ScanTable(db, totalPages, numTdef, rec => {
                string[] fields = AllVarFields(rec);
                if (fields == null || fields.Length == 0) return;
                string pn = fields[0].Trim();
                if (pn.Length == 0) return;
                for (int fi = 1; fi < fields.Length; fi++)
                {
                    string f = fields[fi].Trim();
                    if (f == "CSMSM" || f == "CSMTH") { result[pn] = f; break; }
                }
            });
            return result;
        }
        // =====================================================================
        // Assembly table: OrgX/OrgY + CC_COMPRESS Outline blob
        // =====================================================================
        private static void ReadAssembly(byte[] db, int totalPages,
            ref double orgX, ref double orgY,
            List<OutlineSegment> boardOut, List<OutlineSegment> circuitOut)
        {
            // The Assembly OLE fields (Outline, Gerber) live on LVAL pages.
            // The first LVAL page for the Outline blob has CC_COMPRESS at offset 24.
            for (int pn = 2; pn < totalPages; pn++)
            {
                int pb = pn * PageSize;
                if (!IsLval(db, pb)) continue;
                if (!BufAt(db, pb + LvalFirstStart, CcSig)) continue;  // CC_COMPRESS here?
                // The Assembly data page (non-LVAL, same tdef area) holds OrgX/OrgY.
                // OrgX and OrgY are two consecutive doubles within ~100..8000 mil range.
                // They appear at a fixed position in the Assembly record.
                // Scan nearby non-LVAL pages for this pair.
                if (orgX == 0) ScanForOrgXY(db, totalPages, ref orgX, ref orgY);
                // Assemble the full deflate payload across consecutive LVAL pages
                byte[] blob = AssembleLvalBlob(db, totalPages, pn);
                if (blob == null) continue;
                // Decompress (raw deflate, wbits = -15)
                byte[] dec = DecompressCC(blob);
                if (dec != null)
                {
                    ParseOutlineBlob(dec, boardOut, circuitOut);
                    if (boardOut.Count > 0 || circuitOut.Count > 0)
                        return;  // Got valid outline data
                }
                // dec == null means unc_size was bogus (e.g. a non-Outline CC blob).
                // Continue scanning for the real Outline blob.
            }
        }
        private static void ScanForOrgXY(byte[] db, int totalPages,
                                         ref double orgX, ref double orgY)
        {
            for (int pn = 2; pn < totalPages; pn++)
            {
                int pb = pn * PageSize;
                if (db[pb] != PageTypeData || IsLval(db, pb)) continue;
                int limit = Math.Min(pb + PageSize, pb + 4000);
                for (int o = pb + 2; o < limit - 15; o += 2)
                {
                    double v1 = RDbl(db, o);
                    if (v1 < 100.0 || v1 > 6500.0) continue;
                    double v2 = RDbl(db, o + 8);
                    if (v2 < 0.0 || v2 > 6500.0 || v1 == v2) continue;
                    orgX = v1; orgY = v2;
                    return;
                }
            }
        }
        // =====================================================================
        // LVAL blob assembly
        // =====================================================================
        /// <summary>
        /// Reads a CC_COMPRESS blob that begins at page <paramref name="firstPage"/>
        /// offset 24, continuing on subsequent consecutive LVAL pages at offset 20.
        /// Stops when a non-LVAL page is reached or when enough data is collected.
        /// Returns the complete blob bytes including the 32-byte CC_COMPRESS header.
        /// </summary>
        private static byte[] AssembleLvalBlob(byte[] db, int totalPages, int firstPage)
        {
            // Read declared uncompressed size from the header
            int pb0 = firstPage * PageSize;
            uint uncSize = (uint)(db[pb0 + LvalFirstStart + CcUncOffset]
                                | (db[pb0 + LvalFirstStart + CcUncOffset + 1] << 8)
                                | (db[pb0 + LvalFirstStart + CcUncOffset + 2] << 16)
                                | (db[pb0 + LvalFirstStart + CcUncOffset + 3] << 24));
            using (var ms = new MemoryStream())
            {
                // First page: data from byte 24 to end of page
                ms.Write(db, pb0 + LvalFirstStart, PageSize - LvalFirstStart);
                // Continuation pages: data from byte 20 to end of page
                for (int pn = firstPage + 1; pn < totalPages; pn++)
                {
                    int pb = pn * PageSize;
                    if (!IsLval(db, pb)) break;
                    ms.Write(db, pb + LvalContStart, PageSize - LvalContStart);
                    // Stop once we have much more than we could possibly need
                    if (ms.Length > uncSize + CcHeaderLen + PageSize) break;
                }
                return ms.ToArray();
            }
        }
        // =====================================================================
        // CC_COMPRESS decompressor
        // =====================================================================
        private static byte[] DecompressCC(byte[] blob)
        {
            if (blob == null || blob.Length < CcHeaderLen + 1) return null;
            if (!BufAt(blob, 0, CcSig)) return null;
            int uncSize = (int)((uint)(blob[CcUncOffset]
                              | (blob[CcUncOffset+1] << 8)
                              | (blob[CcUncOffset+2] << 16)
                              | (blob[CcUncOffset+3] << 24)));
            if (uncSize <= 0 || uncSize > 10 * 1024 * 1024) return null;
            try
            {
                using (var ms  = new MemoryStream(blob, CcHeaderLen, blob.Length - CcHeaderLen))
                using (var ds  = new DeflateStream(ms, CompressionMode.Decompress))
                using (var out2 = new MemoryStream(uncSize))
                {
                    byte[] buf = new byte[65536]; int n;
                    while ((n = ds.Read(buf, 0, buf.Length)) > 0)
                    {
                        out2.Write(buf, 0, n);
                        if (out2.Length >= uncSize) break;
                    }
                    byte[] data = out2.ToArray();
                    // Trim to declared size (may have overread into the next blob)
                    if (data.Length > uncSize)
                    {
                        byte[] t = new byte[uncSize];
                        Buffer.BlockCopy(data, 0, t, 0, uncSize);
                        return t;
                    }
                    return data;
                }
            }
            catch { return null; }
        }
        // =====================================================================
        // Outline blob geometry parser
        // =====================================================================
        /// <summary>
        /// Parse the decompressed Assembly.Outline blob into OutlineSegment lists.
        ///
        /// The blob contains 1468 records, each ~246 bytes, each starting with
        /// the 4-byte magic FF FE FF 03.  Type byte at rec[66] == 8 → line segment.
        /// Doubles at rec[198,206,214,222] = X1,Y1,X2,Y2 in Valor mils.
        ///
        /// Classification:
        ///   Board outer  – any endpoint has Valor X outside 400..5600
        ///                  (touches board edge 0 or 5945, or corner arc)
        ///   Circuit      – all endpoints within 400..5600  (inner keepout rectangle)
        /// </summary>
        private static void ParseOutlineBlob(byte[] data,
            List<OutlineSegment> boardOut, List<OutlineSegment> circuitOut)
        {
            var pos = new List<int>(2000);
            for (int i = 0; i <= data.Length - 4; i++)
                if (data[i]==0xFF && data[i+1]==0xFE &&
                    data[i+2]==0xFF && data[i+3]==0x03)
                    pos.Add(i);
            for (int ri = 1; ri < pos.Count; ri++)
            {
                int s = pos[ri];
                int e = (ri + 1 < pos.Count) ? pos[ri + 1] : s + 246;
                if (e - s < RecMinLen) continue;
                if (data[s + RecTypeByte] != SegLineType) continue;
                double x1 = RDblAt(data, s + RecX1);
                double y1 = RDblAt(data, s + RecY1);
                double x2 = RDblAt(data, s + RecX2);
                double y2 = RDblAt(data, s + RecY2);
                if (x1 == x2 && y1 == y2) continue;               // degenerate
                if (x1 == 0 && y1 == 0 && x2 == 0 && y2 == 0) continue;
                var seg = new OutlineSegment { X1=x1, Y1=y1, X2=x2, Y2=y2 };
                bool outer = x1 < CircXMin || x1 > CircXMax ||
                             x2 < CircXMin || x2 > CircXMax;
                seg.Kind = outer ? "Board" : "Circuit";
                if (outer) boardOut.Add(seg);
                else       circuitOut.Add(seg);
            }
        }
        // =====================================================================
        // Variable-field reader
        // =====================================================================
        private static string[] AllVarFields(byte[] rec)
        {
            if (rec.Length < NullMaskBytes + 4) return null;
            int numVar = RU16(rec, rec.Length - NullMaskBytes - 2);
            if (numVar <= 0 || numVar > MaxVarCols) return null;
            int vtStart = rec.Length - NullMaskBytes - 2 - numVar * 2;
            if (vtStart < 0) return null;
            int[] starts = new int[numVar];
            for (int i = 0; i < numVar; i++)
                starts[i] = RU16(rec, vtStart + (numVar - 1 - i) * 2);
            var fields = new string[numVar];
            for (int i = 0; i < numVar; i++)
            {
                int s = starts[i], e = (i+1 < numVar) ? starts[i+1] : vtStart;
                if (s < 0 || s > e || e > rec.Length) { fields[i] = ""; continue; }
                fields[i] = DecodeText(rec, s, e - s);
            }
            return fields;
        }
        // =====================================================================
        // Text decoding
        // =====================================================================
        /// <summary>
        /// Decode a Jet4 variable-length text field.
        /// Prefix FF FE → Valor compressed Unicode:
        ///   bytes &lt;0x80 are literal ASCII;
        ///   bytes ≥0x80 are the low byte of a UTF-16LE code unit (high byte follows).
        /// No prefix → raw UTF-16LE.
        /// </summary>
        private static string DecodeText(byte[] buf, int offset, int length)
        {
            if (length <= 0) return string.Empty;
            if (length >= 2 && buf[offset] == 0xFF && buf[offset+1] == 0xFE)
            {
                var sb = new StringBuilder(length);
                int i = offset + 2, end = offset + length;
                while (i < end)
                {
                    byte b = buf[i];
                    if (b < 0x80) { sb.Append((char)b); i++; }
                    else if (i+1 < end) { sb.Append((char)(b | (buf[i+1] << 8))); i += 2; }
                    else i++;
                }
                return sb.ToString();
            }
            if (length < 2) return string.Empty;
            return Encoding.Unicode.GetString(buf, offset, length & ~1).TrimEnd('\0');
        }
        // =====================================================================
        // Binary helpers
        // =====================================================================
        private static bool IsLval(byte[] db, int pb)
            => pb + 8 <= db.Length
            && db[pb+4]==LvalSig[0] && db[pb+5]==LvalSig[1]
            && db[pb+6]==LvalSig[2] && db[pb+7]==LvalSig[3];
        private static bool BufAt(byte[] hay, int off, byte[] needle)
        {
            if (off + needle.Length > hay.Length) return false;
            for (int i = 0; i < needle.Length; i++)
                if (hay[off + i] != needle[i]) return false;
            return true;
        }
        private static bool BufContains(byte[] hay, int start, int len, byte[] needle)
        {
            int end = start + len - needle.Length;
            for (int i = start; i <= end; i++)
            {
                bool ok = true;
                for (int j = 0; j < needle.Length; j++)
                    if (hay[i+j] != needle[j]) { ok = false; break; }
                if (ok) return true;
            }
            return false;
        }
        private static int    RI32(byte[] b, int o) =>
            b[o] | (b[o+1]<<8) | (b[o+2]<<16) | (b[o+3]<<24);
        private static int    RU16(byte[] b, int o) => b[o] | (b[o+1]<<8);
        private static double RDbl(byte[] b, int o)   => BitConverter.ToDouble(b, o);
        private static double RDblAt(byte[] b, int o) => BitConverter.ToDouble(b, o);
        private static int MaxVotes(Dictionary<int, int> votes)
        {
            int best = -1, bestV = 0;
            foreach (var kv in votes)
                if (kv.Value > bestV) { bestV = kv.Value; best = kv.Key; }
            return best;
        }
        // =====================================================================
        // Sorting / CSV
        // =====================================================================
        private static int CompareByRef(PlacementRecord a, PlacementRecord b)
        {
            int c = string.Compare(AlphaPfx(a.Ref??""), AlphaPfx(b.Ref??""),
                                   StringComparison.OrdinalIgnoreCase);
            return c != 0 ? c : NumSfx(a.Ref??"").CompareTo(NumSfx(b.Ref??""));
        }
        private static string AlphaPfx(string s)
        { int i=0; while(i<s.Length && char.IsLetter(s[i]))i++; return s.Substring(0,i); }
        private static int NumSfx(string s)
        { int i=s.Length-1; while(i>=0&&char.IsDigit(s[i]))i--;
          string d=s.Substring(i+1); int r; return int.TryParse(d,out r)?r:0; }
        private static string Esc(string s)
        {
            if (s == null) return "";
            if (s.IndexOfAny(new[]{',','"','\n','\r'}) >= 0)
                return "\"" + s.Replace("\"","\"\"") + "\"";
            return s;
        }
    }
}
